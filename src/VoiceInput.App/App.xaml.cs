using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using VoiceInput.Core.Activation;
using VoiceInput.Core.Audio;
using VoiceInput.Windows.Audio;
using VoiceInput.Windows.Hotkeys;
using VoiceInput.Windows.Input;
using VoiceInput.Windows.Lifecycle;
using VoiceInput.Windows.Settings;
using VoiceInput.Windows.Targeting;
using VoiceInput.Windows.Transcription;
using Forms = System.Windows.Forms;

namespace VoiceInput.App;

public partial class App : System.Windows.Application, IDisposable, ISettingsHost
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim engineGate = new(1, 1);
    private readonly SettingsStore settingsStore = new();
    private readonly DpapiOpenAiApiKeyProvider apiKeyProvider = new();
    private readonly string? diagnosticLogPath = Environment.GetEnvironmentVariable("VOICE_INPUT_DIAGNOSTIC_LOG");

    private AppSettings settings = new();
    private TranscriptionEngineFactory? engineFactory;
    private AutostartRegistration? autostart;
    private TranscriptionEngineHandle? engine;
    private SettingsWindow? settingsWindow;
    private MainWindow? overlay;
    private SingleInstanceLease? singleInstance;
    private GlobalHotkeyRegistration? hotkey;
    private Forms.NotifyIcon? trayIcon;
    private Forms.ContextMenuStrip? trayMenu;
    private Icon? applicationIcon;
    private Forms.ToolStripLabel? statusItem;
    private Forms.ToolStripMenuItem? openAiItem;
    private Forms.ToolStripMenuItem? localItem;
    private Forms.ToolStripMenuItem? microphoneMenu;
    private DictationWorkflow? workflow;
    private IAudioRecorder? recorder;
    private IRecordingLevelSource? recordingLevelSource;
    private ManualReleaseGate? toggleReleaseGate;
    private string? initializationError;
    private int sessionRunning;
    private bool disposed;

    AppSettings ISettingsHost.Current => settings;

    bool ISettingsHost.HasApiKey => apiKeyProvider.HasKey;

    bool ISettingsHost.LocalEngineAvailable => engineFactory?.IsLocalEngineAvailable == true;

    string ISettingsHost.Version => ApplicationVersion;

    IReadOnlyList<MicrophoneChoice> ISettingsHost.Microphones => AudioDeviceCatalog.ListMicrophones();

    async Task<OpenAiKeyCheckResult> ISettingsHost.CheckApiKeyAsync(string? candidate)
    {
        string key;
        try
        {
            key = string.IsNullOrWhiteSpace(candidate) ? apiKeyProvider.GetApiKey() : candidate.Trim();
        }
        catch (InvalidOperationException exception)
        {
            return new OpenAiKeyCheckResult(OpenAiKeyCheckStatus.Rejected, exception.Message);
        }

        using var http = new HttpClient();
        return await OpenAiKeyCheck.CheckAsync(http, key, lifetime.Token);
    }

    private static string ApplicationVersion
    {
        get
        {
            var informational = typeof(App).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0";
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // VOICE_INPUT_INSTANCE_NAME lets a test run start next to an already running copy.
        singleInstance = new SingleInstanceLease(
            Environment.GetEnvironmentVariable("VOICE_INPUT_INSTANCE_NAME") ?? "Local\\VoiceInput.App");
        if (!singleInstance.IsPrimary)
        {
            singleInstance.Dispose();
            singleInstance = null;
            Shutdown();
            return;
        }

        var executable = Environment.ProcessPath ?? string.Empty;
        autostart = new AutostartRegistration(new RegistryAutostartStore(), executable);
        engineFactory = new TranscriptionEngineFactory(apiKeyProvider, TranscriptionEngineFactory.DefaultWorkerExecutablePath);
        var firstRun = !File.Exists(settingsStore.FilePath);
        settings = settingsStore.Load() with { StartWithWindows = autostart.IsEnabled };
        if (settings.Engine == TranscriptionEngine.Local && !engineFactory.IsLocalEngineAvailable)
        {
            // A settings file from a build that had the local worker: fall back to the engine this install ships.
            settings = settings with { Engine = TranscriptionEngine.OpenAi };
        }


        overlay = new MainWindow();
        hotkey = new GlobalHotkeyRegistration(() => workflow?.CanCancel == true);
        hotkey.Activated += OnHotkeyActivated;
        hotkey.ToggleActivated += OnToggleHotkeyActivated;
        hotkey.CancellationRequested += OnCancellationRequested;

        trayIcon = BuildTrayIcon();
        trayIcon.Visible = true;
        ShowHotkeyConflictWarning();
        _ = StartAsync(firstRun, lifetime.Token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();

        if (hotkey is not null)
        {
            hotkey.Activated -= OnHotkeyActivated;
            hotkey.ToggleActivated -= OnToggleHotkeyActivated;
            hotkey.CancellationRequested -= OnCancellationRequested;
            hotkey.Dispose();
            hotkey = null;
        }

        if (recordingLevelSource is not null)
        {
            recordingLevelSource.RecordingLevelChanged -= OnRecordingLevelChanged;
            recordingLevelSource = null;
        }

        (recorder as IDisposable)?.Dispose();
        recorder = null;

        if (engine is not null)
        {
            var closing = engine;
            engine = null;
            Task.Run(async () => await closing.DisposeAsync()).GetAwaiter().GetResult();
        }

        settingsWindow?.Close();
        settingsWindow = null;

        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
            trayIcon.ContextMenuStrip = null;
            trayIcon.Dispose();
            trayIcon = null;
        }

        trayMenu?.Dispose();
        trayMenu = null;
        applicationIcon?.Dispose();
        applicationIcon = null;
        overlay?.CloseForShutdown();
        overlay = null;
        singleInstance?.Dispose();
        singleInstance = null;
        engineGate.Dispose();
        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    string? ISettingsHost.SaveApiKey(string apiKey)
    {
        try
        {
            apiKeyProvider.Save(apiKey);
            return null;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            Log($"api-key-save-error: {exception.GetType().Name}");
            return "Не удалось сохранить ключ. Проверьте доступ к папке профиля.";
        }
    }

    void ISettingsHost.DeleteApiKey()
    {
        try
        {
            apiKeyProvider.Delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log($"api-key-delete-error: {exception.GetType().Name}");
        }
    }

    async Task<string?> ISettingsHost.ApplyAsync(AppSettings next)
    {
        if (sessionRunning != 0)
        {
            return "Идёт диктовка. Подождите её завершения.";
        }

        var engineChanged = next.Engine != settings.Engine || engine is null;
        var languageChanged = next.Language != settings.Language;
        if (next.StartWithWindows != settings.StartWithWindows)
        {
            try
            {
                autostart?.Set(next.StartWithWindows);
            }
            catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return exception.Message;
            }
        }

        settings = next;
        try
        {
            settingsStore.Save(next);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log($"settings-save-error: {exception.GetType().Name}");
        }

        UpdateEngineChecks();
        if (!engineChanged && !languageChanged && initializationError is null)
        {
            return null;
        }

        await ReinitializeEngineAsync(lifetime.Token);
        return initializationError;
    }

    private async Task StartAsync(bool firstRun, CancellationToken cancellationToken)
    {
        UpdateEngineChecks();
        await ReinitializeEngineAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // A new install has no key and the default engine needs one: say so instead of failing silently.
        if (firstRun && settings.Engine == TranscriptionEngine.OpenAi && !apiKeyProvider.HasKey)
        {
            ShowSettings();
        }
    }

    private async Task ReinitializeEngineAsync(CancellationToken cancellationToken)
    {
        if (engineFactory is null || overlay is null)
        {
            return;
        }

        await engineGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        TranscriptionEngineHandle? pending = null;
        try
        {
            initializationError = null;
            workflow = null;
            UpdateStatus("Запускаю распознавание…");

            var progress = new Progress<string>(status =>
            {
                Log($"startup-progress: {status}");
                UpdateStatus(status);
            });
            pending = await engineFactory.CreateAsync(settings, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            recorder ??= CreateRecorder();
            var textInserter = Environment.GetEnvironmentVariable("VOICE_INPUT_E2E_FORCE_CLIPBOARD") == "1"
                ? (ITextInserter)new WindowsClipboardTextInserter()
                : new WindowsTextInserter();
            workflow = new DictationWorkflow(
                new ForegroundTargetCapture(),
                overlay,
                new ModifierReleaseGate(),
                textInserter,
                new SystemAsyncDelay(),
                recorder,
                pending.Transcriber);

            var replaced = engine;
            engine = pending;
            pending = null;
            if (replaced is not null)
            {
                await replaced.DisposeAsync();
            }

            Log("dictation-ready");
            UpdateStatus(settings.Engine == TranscriptionEngine.OpenAi ? "Готов · OpenAI" : "Готов · на этом компьютере");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log($"initialization-error: {exception.GetType().Name}: {exception.Message}");
            initializationError = exception.Message;
            UpdateStatus("Нужна настройка");
            trayIcon?.ShowBalloonTip(
                8_000,
                "Voice Input — не удалось запустить распознавание",
                exception.Message.Length <= 240 ? exception.Message : exception.Message[..240],
                Forms.ToolTipIcon.Warning);
        }
        finally
        {
            if (pending is not null)
            {
                await pending.DisposeAsync();
            }

            if (!disposed)
            {
                engineGate.Release();
            }
        }
    }

    private IAudioRecorder CreateRecorder()
    {
        var fixturePath = Environment.GetEnvironmentVariable("VOICE_INPUT_PCM_FIXTURE");
        // The factory reads the setting at every recording, so a new microphone applies from the next dictation.
        IAudioRecorder created = string.IsNullOrWhiteSpace(fixturePath)
            ? new WasapiPushToTalkRecorder(() => settings.MicrophoneId)
            : new PcmFixtureAudioRecorder(fixturePath);
        if (created is IRecordingLevelSource levelSource)
        {
            recordingLevelSource = levelSource;
            recordingLevelSource.RecordingLevelChanged += OnRecordingLevelChanged;
        }

        return created;
    }

    private Forms.NotifyIcon BuildTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        TrayMenuRenderer.Apply(menu);
        trayMenu = menu;

        statusItem = new Forms.ToolStripLabel("Запускаю распознавание…")
        {
            AutoSize = true,
            Padding = new Forms.Padding(8, 7, 8, 6),
            ForeColor = Forms.SystemInformation.HighContrast ? System.Drawing.SystemColors.MenuText : TrayMenuRenderer.Text,
            Font = new Font(menu.Font, System.Drawing.FontStyle.Bold),
        };
        menu.Items.Add(statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var engineMenu = new Forms.ToolStripMenuItem("Распознавание");
        openAiItem = new Forms.ToolStripMenuItem("OpenAI, через интернет");
        localItem = new Forms.ToolStripMenuItem("На этом компьютере (GigaAM)");
        openAiItem.Click += async (_, _) => await SwitchEngineAsync(TranscriptionEngine.OpenAi);
        localItem.Click += async (_, _) => await SwitchEngineAsync(TranscriptionEngine.Local);
        engineMenu.DropDownItems.Add(openAiItem);
        engineMenu.DropDownItems.Add(localItem);
        engineMenu.Visible = engineFactory?.IsLocalEngineAvailable == true;
        menu.Items.Add(engineMenu);

        microphoneMenu = new Forms.ToolStripMenuItem("Микрофон");
        microphoneMenu.DropDownOpening += (_, _) => FillMicrophoneMenu();
        microphoneMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("Загрузка…") { Enabled = false });
        menu.Items.Add(microphoneMenu);

        var settingsItem = new Forms.ToolStripMenuItem("Настройки…");
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        var hotkeysItem = new Forms.ToolStripMenuItem("Горячие клавиши");
        hotkeysItem.DropDownItems.Add(new Forms.ToolStripMenuItem("Удерживать для записи — Ctrl + Shift + Space") { Enabled = false });
        hotkeysItem.DropDownItems.Add(new Forms.ToolStripMenuItem("Начать или завершить — Ctrl + Shift + K") { Enabled = false });
        hotkeysItem.DropDownItems.Add(new Forms.ToolStripMenuItem("Отменить диктовку — Esc") { Enabled = false });
        menu.Items.Add(hotkeysItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("Закрыть Voice Input");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        applicationIcon = LoadApplicationIcon();
        var icon = new Forms.NotifyIcon
        {
            Text = "Voice Input — запуск",
            Icon = applicationIcon,
            ContextMenuStrip = menu,
        };
        icon.DoubleClick += (_, _) => ShowSettings();
        return icon;
    }

    // Built when the submenu opens, so a microphone plugged in a minute ago is already there.
    private void FillMicrophoneMenu()
    {
        if (microphoneMenu is null)
        {
            return;
        }

        microphoneMenu.DropDownItems.Clear();
        var current = settings.MicrophoneId;
        var defaultItem = new Forms.ToolStripMenuItem("По умолчанию в Windows")
        {
            Checked = string.IsNullOrWhiteSpace(current),
        };
        defaultItem.Click += async (_, _) => await SwitchMicrophoneAsync(null);
        microphoneMenu.DropDownItems.Add(defaultItem);

        foreach (var microphone in AudioDeviceCatalog.ListMicrophones())
        {
            var item = new Forms.ToolStripMenuItem(microphone.Name)
            {
                Checked = string.Equals(microphone.Id, current, StringComparison.Ordinal),
            };
            var id = microphone.Id;
            item.Click += async (_, _) => await SwitchMicrophoneAsync(id);
            microphoneMenu.DropDownItems.Add(item);
        }
    }

    private async Task SwitchMicrophoneAsync(string? microphoneId)
    {
        var error = await ((ISettingsHost)this).ApplyAsync(settings with { MicrophoneId = microphoneId });
        if (error is not null)
        {
            await ShowTransientErrorAsync(error);
        }
    }

    private void UpdateEngineChecks()
    {
        if (openAiItem is not null)
        {
            openAiItem.Checked = settings.Engine == TranscriptionEngine.OpenAi;
        }

        if (localItem is not null)
        {
            localItem.Checked = settings.Engine == TranscriptionEngine.Local;
        }
    }

    private async Task SwitchEngineAsync(TranscriptionEngine target)
    {
        if (target == settings.Engine && engine is not null)
        {
            return;
        }

        var error = await ((ISettingsHost)this).ApplyAsync(settings with { Engine = target });
        if (error is not null)
        {
            await ShowTransientErrorAsync(error);
        }
    }

    private void ShowSettings()
    {
        if (disposed)
        {
            return;
        }

        if (settingsWindow is { IsVisible: true })
        {
            settingsWindow.Activate();
            return;
        }

        settingsWindow = new SettingsWindow(this);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
        settingsWindow.Activate();
    }

    private static Icon LoadApplicationIcon()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var associatedIcon = Icon.ExtractAssociatedIcon(processPath);
            if (associatedIcon is not null)
            {
                return associatedIcon;
            }
        }

        var resource = GetResourceStream(new Uri("pack://application:,,,/VoiceInput.ico", UriKind.Absolute))
            ?? throw new InvalidOperationException("Embedded Voice Input icon is unavailable.");
        using var stream = resource.Stream;
        using var resourceIcon = new Icon(stream);
        return (Icon)resourceIcon.Clone();
    }

    private async void OnHotkeyActivated(object? sender, EventArgs e)
    {
        await RunDictationAsync();
    }

    private async void OnToggleHotkeyActivated(object? sender, EventArgs e)
    {
        var activeGate = Volatile.Read(ref toggleReleaseGate);
        if (activeGate is not null)
        {
            if (activeGate.Release())
            {
                Log("toggle-hotkey-stop");
            }

            return;
        }

        var sessionGate = new ManualReleaseGate();
        activeGate = Interlocked.CompareExchange(ref toggleReleaseGate, sessionGate, null);
        if (activeGate is not null)
        {
            activeGate.Release();
            return;
        }

        Log("toggle-hotkey-start");
        try
        {
            await RunDictationAsync(sessionGate);
        }
        finally
        {
            Interlocked.CompareExchange(ref toggleReleaseGate, null, sessionGate);
        }
    }

    private async Task RunDictationAsync(IModifierReleaseGate? sessionReleaseGate = null)
    {
        var current = workflow;
        Log($"hotkey workflow-ready={current is not null}");
        if (overlay is null)
        {
            return;
        }

        if (current is null)
        {
            if (initializationError is not null)
            {
                await ShowTransientErrorAsync(initializationError);
            }
            else
            {
                await ShowTransientStatusAsync("Voice Input запускается", "Повторите через несколько секунд");
            }

            return;
        }

        if (Interlocked.CompareExchange(ref sessionRunning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            hotkey?.EnableCancellation();
            if (sessionReleaseGate is null)
            {
                await current.TryActivateAsync(lifetime.Token);
            }
            else
            {
                await current.TryActivateAsync(sessionReleaseGate, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            Log("dictation-cancelled");
            await ShowTransientStatusAsync("Отменено", "Текст не вставлен");
        }
        catch (TargetFocusChangedException)
        {
            await ShowTransientStatusAsync("Окно сменилось", "Текст не вставлен");
        }
        catch (Exception exception)
        {
            Log($"dictation-error: {exception.GetType().Name}");
            await ShowTransientErrorAsync(exception.Message);
        }
        finally
        {
            hotkey?.DisableCancellation();
            Interlocked.Exchange(ref sessionRunning, 0);
        }
    }

    private void OnCancellationRequested(object? sender, EventArgs e)
    {
        if (workflow?.CancelActive() == true)
        {
            Log("escape-cancel-requested");
        }
    }

    private void OnRecordingLevelChanged(float level)
    {
        overlay?.SetRecordingLevel(level);
    }

    private async Task ShowTransientErrorAsync(string message)
    {
        if (overlay is null || lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            overlay.ShowError(message);
        }
        catch (Exception exception)
        {
            Log($"overlay-error-display: {exception.GetType().Name}");
            return;
        }

        await HideOverlayAfterDelayAsync(TimeSpan.FromSeconds(3));
    }

    private async Task ShowTransientStatusAsync(string title, string detail)
    {
        if (overlay is null || lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            overlay.ShowStatus(title, detail);
        }
        catch (Exception exception)
        {
            Log($"overlay-status-display: {exception.GetType().Name}");
            return;
        }

        await HideOverlayAfterDelayAsync(TimeSpan.FromSeconds(2));
    }

    private async Task HideOverlayAfterDelayAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                overlay?.Hide();
            }
            catch (Exception exception)
            {
                Log($"overlay-hide: {exception.GetType().Name}");
            }
        }
    }

    private void ShowHotkeyConflictWarning()
    {
        var message = hotkey?.GetConflictMessage();
        if (string.IsNullOrWhiteSpace(message) || trayIcon is null)
        {
            return;
        }

        Log($"hotkey-registration-warning: hold={hotkey!.HoldHotkeyError} toggle={hotkey.ToggleHotkeyError}");
        trayIcon.ShowBalloonTip(8_000, "Voice Input — горячая клавиша занята", message, Forms.ToolTipIcon.Warning);
    }

    private void UpdateStatus(string status)
    {
        if (disposed)
        {
            return;
        }

        if (statusItem is not null)
        {
            statusItem.Text = status;
        }

        if (trayIcon is not null)
        {
            const int maximumTooltipLength = 63;
            var tooltip = $"Voice Input — {status}";
            trayIcon.Text = tooltip.Length <= maximumTooltipLength ? tooltip : tooltip[..maximumTooltipLength];
        }
    }

    private void Log(string message) => DiagnosticLog.TryAppend(diagnosticLogPath, message);
}

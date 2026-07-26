using System.Drawing;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;
using VoiceInput.Core.Activation;
using VoiceInput.Core.Audio;
using VoiceInput.Windows.Audio;
using VoiceInput.Windows.Hotkeys;
using VoiceInput.Windows.Input;
using VoiceInput.Windows.Lifecycle;
using VoiceInput.Windows.Targeting;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.App;

public partial class App : System.Windows.Application, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private MainWindow? overlay;
    private SingleInstanceLease? singleInstance;
    private GlobalHotkeyRegistration? hotkey;
    private Forms.NotifyIcon? trayIcon;
    private Icon? applicationIcon;
    private Forms.ToolStripMenuItem? statusItem;
    private DictationWorkflow? workflow;
    private IAudioRecorder? recorder;
    private GigaAmWorkerClient? asrClient;
    private string? initializationError;
    private int sessionRunning;
    private readonly string? diagnosticLogPath = Environment.GetEnvironmentVariable("VOICE_INPUT_DIAGNOSTIC_LOG");
    private bool disposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstance = new SingleInstanceLease("Local\\VoiceInput.App");
        if (!singleInstance.IsPrimary)
        {
            singleInstance.Dispose();
            singleInstance = null;
            Shutdown();
            return;
        }

        overlay = new MainWindow();
        hotkey = new GlobalHotkeyRegistration();
        hotkey.Activated += OnHotkeyActivated;
        hotkey.CancellationRequested += OnCancellationRequested;

        trayIcon = BuildTrayIcon();
        trayIcon.Visible = true;
        trayIcon.ShowBalloonTip(
            5_000,
            "Voice Input",
            "Подготавливаю локальное распознавание. Первый запуск может занять несколько минут.",
            Forms.ToolTipIcon.Info);
        _ = InitializeDictationAsync(lifetime.Token);
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
            hotkey.CancellationRequested -= OnCancellationRequested;
            hotkey.Dispose();
            hotkey = null;
        }

        (recorder as IDisposable)?.Dispose();
        recorder = null;

        if (asrClient is not null)
        {
            Task.Run(async () => await asrClient.DisposeAsync()).GetAwaiter().GetResult();
            asrClient = null;
        }

        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        applicationIcon?.Dispose();
        applicationIcon = null;

        overlay?.Close();
        overlay = null;
        singleInstance?.Dispose();
        singleInstance = null;
        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task InitializeDictationAsync(CancellationToken cancellationToken)
    {
        GigaAmWorkerClient? pendingClient = null;
        IAudioRecorder? pendingRecorder = null;
        try
        {
            var progress = new Progress<string>(UpdateStatus);
            var assets = await new LocalAsrAssetProvisioner().EnsureAsync(progress, cancellationToken);
            var workerExecutable = Path.Combine(AppContext.BaseDirectory, "worker", "VoiceInput.Asr.Worker.exe");

            pendingClient = new GigaAmWorkerClient(workerExecutable, assets.RuntimeDirectory, assets.ModelPath);
            UpdateStatus("Загружаю GigaAM…");
            await pendingClient.StartAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var fixturePath = Environment.GetEnvironmentVariable("VOICE_INPUT_PCM_FIXTURE");
            pendingRecorder = string.IsNullOrWhiteSpace(fixturePath)
                ? new WasapiPushToTalkRecorder()
                : new PcmFixtureAudioRecorder(fixturePath);
            ITextInserter textInserter = Environment.GetEnvironmentVariable("VOICE_INPUT_E2E_FORCE_CLIPBOARD") == "1"
                ? new WindowsClipboardTextInserter()
                : new WindowsTextInserter();
            var readyWorkflow = new DictationWorkflow(
                new ForegroundTargetCapture(),
                overlay ?? throw new InvalidOperationException("The overlay is unavailable."),
                new ModifierReleaseGate(),
                textInserter,
                new SystemAsyncDelay(),
                pendingRecorder,
                new SegmentingTranscriber(pendingClient));
            cancellationToken.ThrowIfCancellationRequested();

            asrClient = pendingClient;
            recorder = pendingRecorder;
            workflow = readyWorkflow;
            pendingClient = null;
            pendingRecorder = null;

            Log("dictation-ready");
            UpdateStatus("Готово — Ctrl + Shift + Space");
            trayIcon?.ShowBalloonTip(
                5_000,
                "Voice Input готов",
                "Удерживайте Ctrl + Shift + Space, говорите и отпустите клавиши. Esc отменяет диктовку.",
                Forms.ToolTipIcon.Info);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log($"initialization-error: {exception}");
            initializationError = exception.Message;
            UpdateStatus("Ошибка локальной модели");
            trayIcon?.ShowBalloonTip(
                8_000,
                "Voice Input — ошибка запуска",
                exception.Message.Length <= 240 ? exception.Message : exception.Message[..240],
                Forms.ToolTipIcon.Error);
        }
        finally
        {
            (pendingRecorder as IDisposable)?.Dispose();
            if (pendingClient is not null)
            {
                await pendingClient.DisposeAsync();
            }
        }
    }

    private Forms.NotifyIcon BuildTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        statusItem = new Forms.ToolStripMenuItem("Подготавливаю локальную модель…")
        {
            Enabled = false,
        };
        menu.Items.Add(statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        var exitItem = new Forms.ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        var processPath = Environment.ProcessPath;
        applicationIcon = string.IsNullOrWhiteSpace(processPath)
            ? null
            : Icon.ExtractAssociatedIcon(processPath);

        return new Forms.NotifyIcon
        {
            Text = "Voice Input — подготовка модели",
            Icon = applicationIcon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
        };
    }

    private async void OnHotkeyActivated(object? sender, EventArgs e)
    {
        Log($"hotkey workflow-ready={workflow is not null}");
        if (overlay is null)
        {
            return;
        }

        if (workflow is null)
        {
            if (initializationError is not null)
            {
                await ShowTransientErrorAsync(initializationError);
            }
            else
            {
                await ShowTransientStatusAsync("Готовлю локальную модель", "Первый запуск может занять несколько минут");
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
            await workflow.TryActivateAsync(lifetime.Token);
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
            await ShowTransientErrorAsync("Активное окно изменилось — вставка отменена");
        }
        catch (Exception exception)
        {
            Log($"dictation-error: {exception}");
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

    private async Task ShowTransientErrorAsync(string message)
    {
        if (overlay is null || lifetime.IsCancellationRequested)
        {
            return;
        }

        overlay.ShowError(message);
        await HideOverlayAfterDelayAsync(TimeSpan.FromSeconds(3));
    }

    private async Task ShowTransientStatusAsync(string title, string detail)
    {
        if (overlay is null || lifetime.IsCancellationRequested)
        {
            return;
        }

        overlay.ShowStatus(title, detail);
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
            overlay?.Hide();
        }
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
            trayIcon.Text = tooltip.Length <= maximumTooltipLength
                ? tooltip
                : tooltip[..maximumTooltipLength];
        }
    }

    private void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(diagnosticLogPath))
        {
            return;
        }

        try
        {
            File.AppendAllText(diagnosticLogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}

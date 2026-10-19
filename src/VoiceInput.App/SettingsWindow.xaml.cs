using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VoiceInput.Windows.Audio;
using VoiceInput.Windows.Settings;
using VoiceInput.Windows.Transcription;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Tone = VoiceInput.App.EngineCopy.Tone;

namespace VoiceInput.App;

/// <summary>What the settings window needs from the application. The window never touches engines or files itself.</summary>
internal interface ISettingsHost
{
    AppSettings Current { get; }

    bool HasApiKey { get; }

    /// <summary>Local recognition is selectable even before the model is downloaded.</summary>
    bool LocalEngineAvailable { get; }

    string Version { get; }

    /// <summary>Input devices at the moment of the call.</summary>
    IReadOnlyList<MicrophoneChoice> Microphones { get; }

    /// <summary>The local model: its download, its state and its removal.</summary>
    ILocalModelHost LocalModel { get; }

    /// <summary>The user's own OpenAI-compatible server: its key and the connection check.</summary>
    ICustomServerHost CustomServer { get; }

    /// <summary>Applies new settings; returns a user-facing error message, or null on success.</summary>
    Task<string?> ApplyAsync(AppSettings next);

    /// <summary>Stores the API key; returns a user-facing error message, or null on success.</summary>
    string? SaveApiKey(string apiKey);

    void DeleteApiKey();

    /// <summary>Asks OpenAI whether the typed key (or, when empty, the saved one) works.</summary>
    Task<OpenAiKeyCheckResult> CheckApiKeyAsync(string? candidate);
}

public partial class SettingsWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private const string RepositoryUrl = "https://github.com/pavel-logachev/voice-input";
    private const string ErrorTag = "Error";

    private static readonly (string? Code, string Label)[] Languages =
    [
        (null, "Определять автоматически"),
        ("ru", "Русский"),
        ("en", "English"),
        ("uk", "Українська"),
        ("de", "Deutsch"),
    ];

    private readonly ISettingsHost host;
    private readonly ILocalModelHost localModel;
    private readonly ICustomServerHost customServer;
    private readonly UiPalette palette = Theme.Current;
    private bool loading;
    private LocalModelProgress localProgress;
    private string? localNotice;
    private bool customUrlEdited;
    private bool customModelEdited;
    private bool customUrlShowsError;
    private bool customModelShowsError;
    private string? customKeyError;
    private bool customCheckRunning;
    private int customCheckVersion;

    internal SettingsWindow(ISettingsHost host)
    {
        this.host = host;
        localModel = host.LocalModel;
        customServer = host.CustomServer;
        localProgress = localModel.Progress;
        InitializeComponent();
        Theme.Apply(this);
        HeaderIcon.Source = LoadHeaderIcon();
        SourceInitialized += (_, _) =>
        {
            UiEnvironment.ApplyTextFormatting(this);
            MatchTitleBar();
        };
        DpiChanged += (_, _) => UiEnvironment.ApplyTextFormatting(this);
        ApplyEngineTexts();
        OpenAiRadio.Checked += OnEngineChanged;
        LocalRadio.Checked += OnEngineChanged;
        CustomRadio.Checked += OnEngineChanged;
        AutostartBox.Click += OnAutostartClicked;
        SaveKeyButton.Click += OnSaveKeyClicked;
        CheckKeyButton.Click += OnCheckKeyClicked;
        DeleteKeyButton.Click += OnDeleteKeyClicked;
        LanguageBox.SelectionChanged += OnLanguageChanged;
        MicrophoneBox.SelectionChanged += OnMicrophoneChanged;
        KeyBox.PasswordChanged += (_, _) => RefreshKeyButtons();
        KeyBox.PreviewKeyDown += OnKeyBoxKeyDown;
        LocalActionButton.Click += OnLocalActionClicked;
        ConfirmDeleteButton.Click += OnConfirmDeleteClicked;
        ConfirmCancelButton.Click += (_, _) => HideDeleteConfirmation();
        CustomUrlBox.TextChanged += (_, _) => OnCustomFieldEdited(ref customUrlEdited);
        CustomModelBox.TextChanged += (_, _) => OnCustomFieldEdited(ref customModelEdited);
        CustomUrlBox.LostFocus += (_, _) => _ = CommitCustomFieldsAsync();
        CustomModelBox.LostFocus += (_, _) => _ = CommitCustomFieldsAsync();
        CustomUrlBox.PreviewKeyDown += OnCustomFieldKeyDown;
        CustomModelBox.PreviewKeyDown += OnCustomFieldKeyDown;
        CustomKeyBox.PasswordChanged += (_, _) => OnCustomKeyTyped();
        CustomKeyBox.PreviewKeyDown += OnCustomKeyBoxKeyDown;
        CustomKeySaveButton.Click += OnCustomKeySaveClicked;
        CustomKeyDeleteButton.Click += OnCustomKeyDeleteClicked;
        CustomCheckButton.Click += async (_, _) => await RunCustomCheckAsync();
        PreviewKeyDown += OnWindowPreviewKeyDown;
        localModel.ProgressChanged += OnLocalProgressChanged;
        Closing += (_, _) => _ = CommitCustomFieldsAsync();
        Closed += (_, _) => { InvalidateCustomCheck(); localModel.ProgressChanged -= OnLocalProgressChanged; };
        SizeChanged += (_, _) => KeepOnScreen();
        SiteButton.Click += (_, _) => Open(RepositoryUrl);
        IssueButton.Click += (_, _) => Open(RepositoryUrl + "/issues/new");
        CloseButton.Click += (_, _) => Close();
        VersionText.Text = host.Version;
        // Never taller than the screen; the content scrolls on small displays.
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 24);
        if (!host.LocalEngineAvailable)
        {
            // A build without the local worker: two choices only.
            LocalRadio.Visibility = Visibility.Collapsed;
            CustomDivider.Visibility = Visibility.Collapsed;
        }

        if (palette.HighContrast)
        {
            foreach (var chip in new[] { KeyChip, CustomKeyChip })
            {
                chip.BorderThickness = new Thickness(1);
                chip.BorderBrush = Theme.Brush(palette.Text);
            }
        }

        ConfirmLayer.Background = Theme.Brush(palette.HighContrast ? palette.Window : Theme.WithAlpha(palette.Window, 0xC8));
        CustomUrlBox.Text = host.Current.CustomBaseUrl ?? string.Empty;
        CustomModelBox.Text = host.Current.CustomModel ?? string.Empty;
        customUrlEdited = false;
        customModelEdited = false;
        Reload();
        ShowLocalProgress(localProgress);
    }

    internal void Reload()
    {
        loading = true;
        try
        {
            var settings = host.Current;
            OpenAiRadio.IsChecked = settings.Engine == TranscriptionEngine.OpenAi;
            LocalRadio.IsChecked = settings.Engine == TranscriptionEngine.Local;
            CustomRadio.IsChecked = settings.Engine == TranscriptionEngine.CustomOpenAiCompatible;
            // Only the chosen engine explains itself; the others keep their explanation for screen readers and tooltips.
            OpenAiDescription.Visibility = Show(settings.Engine == TranscriptionEngine.OpenAi);
            LocalDescription.Visibility = Show(settings.Engine == TranscriptionEngine.Local);
            CustomDescription.Visibility = Show(settings.Engine == TranscriptionEngine.CustomOpenAiCompatible);
            KeyCard.Visibility = Show(settings.Engine == TranscriptionEngine.OpenAi);
            LocalPanel.Visibility = Show(settings.Engine == TranscriptionEngine.Local);
            CustomPanel.Visibility = Show(settings.Engine == TranscriptionEngine.CustomOpenAiCompatible);
            AutostartBox.IsChecked = settings.StartWithWindows;
            FillLanguages(settings.Language);
            FillMicrophones(settings.MicrophoneId);
            RefreshKeyState();
            RefreshCustomKeyState();
            RefreshCustomNotes();
        }
        finally
        {
            loading = false;
        }
    }

    /// <summary>Shows the delete question as the "Удалить модель" button does. Also used by the preview tool.</summary>
    internal void ShowDeleteConfirmation()
    {
        ConfirmLayer.Visibility = Visibility.Visible;
        Scroller.IsEnabled = false;
        // The safe answer has the focus: Enter or Space do not delete anything.
        Dispatcher.BeginInvoke(() => ConfirmCancelButton.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Checks the own server the way the "Проверить" button does. Also used by the preview tool.</summary>
    internal async Task RunCustomCheckAsync()
    {
        if (!DpapiCustomApiKeyProvider.IsValidKey(CustomKeyBox.Password))
        {
            customKeyError = EngineCopy.Text("custom.key.error.invalidChars");
            RefreshCustomNotes();
            Announce(CustomKeyNote);
            return;
        }
        customUrlShowsError = true;
        customModelShowsError = true;
        RefreshCustomNotes();
        var url = CustomUrlBox.Text.Trim();
        var model = CustomModelBox.Text.Trim();
        if (EngineCopy.UrlErrorKey(url) is not null)
        {
            CustomUrlBox.Focus();
            return;
        }

        if (EngineCopy.ModelErrorKey(model) is not null)
        {
            CustomModelBox.Focus();
            return;
        }

        var version = ++customCheckVersion;
        customCheckRunning = true;
        RefreshCustomButtons();
        ShowCustomResult(EngineCopy.Text("custom.check.running"), Tone.Neutral);
        try
        {
            await CommitCustomFieldsAsync();
            if (version != customCheckVersion) return;
            var typed = CustomKeyBox.Password;
            var result = await customServer.CheckAsync(url, model, string.IsNullOrWhiteSpace(typed) ? null : typed);
            if (version == customCheckVersion)
            {
                if (result.ErrorCode == TranscriptionErrorCode.InvalidKeyCharacters)
                {
                    customKeyError = EngineCopy.Text("custom.key.error.invalidChars");
                    RefreshCustomNotes();
                    ShowCustomResult(string.Empty, Tone.Neutral);
                }
                else ShowCustomResult(EngineCopy.CheckResult(result), EngineCopy.CheckTone(result.Status));
            }
        }
        catch (OperationCanceledException)
        {
            // The input changed or the application is closing.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (version == customCheckVersion)
                ShowCustomResult(EngineCopy.Text("custom.check.failed.invalidResponse"), Tone.Bad);
        }
        finally
        {
            if (version == customCheckVersion)
            {
                customCheckRunning = false;
                RefreshCustomButtons();
            }
        }
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    // The largest image of the icon: the default pick is a small one, and it blurs at 150–200 %.
    private static BitmapFrame? LoadHeaderIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/VoiceInput.ico", UriKind.Absolute));
        if (resource is null)
        {
            return null;
        }

        using var stream = resource.Stream;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames.MaxBy(frame => frame.PixelWidth);
    }

    // Every text of the engine section comes from the approved string table.
    private void ApplyEngineTexts()
    {
        static string T(string key) => EngineCopy.Text(key);
        EngineCaption.Text = T("engine.section.title");
        OpenAiTitle.Text = T("engine.option.openai.title");
        OpenAiDescription.Text = T("engine.option.openai.desc");
        LocalTitle.Text = T("engine.option.local.title");
        LocalDescription.Text = T("engine.option.local.desc");
        CustomTitle.Text = T("engine.option.custom.title");
        CustomDescription.Text = T("engine.option.custom.desc");
        foreach (var (radio, title, description) in new[]
        {
            (OpenAiRadio, OpenAiTitle, OpenAiDescription),
            (LocalRadio, LocalTitle, LocalDescription),
            (CustomRadio, CustomTitle, CustomDescription),
        })
        {
            AutomationProperties.SetName(radio, title.Text);
            AutomationProperties.SetHelpText(radio, description.Text);
            radio.ToolTip = description.Text;
        }

        LocalModelName.Text = T("local.model.name");
        LocalPrivacy.Text = T("local.privacy.note");
        ConfirmTitle.Text = T("local.delete.confirm.title");
        ConfirmText.Text = T("local.delete.confirm.text");
        ConfirmDeleteButton.Content = T("local.delete.confirm.ok");
        ConfirmCancelButton.Content = T("local.delete.confirm.cancel");
        AutomationProperties.SetName(ConfirmCard, ConfirmTitle.Text);
        CustomUrlLabel.Text = T("custom.url.label");
        CustomModelLabel.Text = T("custom.model.label");
        CustomKeyLabel.Text = T("custom.key.label");
        CustomKeySaveButton.Content = T("custom.key.action.save");
        CustomKeyDeleteButton.Content = T("custom.key.action.delete");
        CustomCheckButton.Content = T("custom.check.action");
        CustomPrivacy.Text = T("custom.privacy.note");
    }

    private void FillLanguages(string? selected)
    {
        LanguageBox.Items.Clear();
        foreach (var (code, label) in Languages)
        {
            var item = new ComboBoxItem { Content = label, Tag = code };
            LanguageBox.Items.Add(item);
            if (string.Equals(code, selected, StringComparison.Ordinal))
            {
                LanguageBox.SelectedItem = item;
            }
        }

        LanguageBox.SelectedItem ??= LanguageBox.Items[0];
    }

    private void FillMicrophones(string? selectedId)
    {
        MicrophoneBox.Items.Clear();
        var defaultItem = new ComboBoxItem { Content = "По умолчанию в Windows", Tag = null };
        MicrophoneBox.Items.Add(defaultItem);
        ComboBoxItem? selected = string.IsNullOrWhiteSpace(selectedId) ? defaultItem : null;
        foreach (var microphone in host.Microphones)
        {
            var item = new ComboBoxItem { Content = microphone.Name, Tag = microphone.Id, ToolTip = microphone.Name };
            MicrophoneBox.Items.Add(item);
            if (string.Equals(microphone.Id, selectedId, StringComparison.Ordinal))
            {
                selected = item;
            }
        }

        // A saved microphone that is unplugged right now: keep the choice visible, Windows default is used meanwhile.
        if (selected is null)
        {
            selected = new ComboBoxItem { Content = "Не подключен — работает микрофон Windows", Tag = selectedId };
            MicrophoneBox.Items.Add(selected);
        }

        MicrophoneBox.SelectedItem = selected;
    }

    private void RefreshKeyState()
    {
        var has = host.HasApiKey;
        PaintChip(KeyChip, KeyChipDot, KeyChipText, has ? "Ключ сохранен" : "Ключ не задан", has ? Tone.Good : Tone.Warn);
        KeyState.Text = has
            ? "Звук уходит в OpenAI только в момент диктовки. Ключ зашифрован и доступен только вашей учетной записи Windows."
            : "Ключ начинается с «sk-», создать его можно на platform.openai.com. Звук уходит в OpenAI только в момент диктовки.";
        KeyPlaceholder.Text = has ? "Новый ключ — чтобы заменить" : "sk-…";
        RefreshKeyButtons();
    }

    private void PaintChip(Border chip, System.Windows.Shapes.Ellipse dot, TextBlock label, string text, Tone tone)
    {
        var colour = ToneColour(tone);
        label.Text = text;
        label.Foreground = Theme.Brush(palette.HighContrast ? palette.Text : colour);
        dot.Fill = Theme.Brush(colour);
        chip.Background = Theme.Brush(palette.HighContrast ? palette.Window : Theme.WithAlpha(colour, 0x24));
    }

    private void RefreshKeyButtons()
    {
        var typed = !string.IsNullOrWhiteSpace(KeyBox.Password);
        KeyPlaceholder.Visibility = KeyBox.SecurePassword.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SaveKeyButton.IsEnabled = typed;
        CheckKeyButton.IsEnabled = typed || host.HasApiKey;
        DeleteKeyButton.IsEnabled = host.HasApiKey;
    }

    // Enter in the key field saves the key; without this the default button would close the window instead.
    private void OnKeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && SaveKeyButton.IsEnabled)
        {
            e.Handled = true;
            OnSaveKeyClicked(sender, e);
        }
    }

    private async void OnEngineChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            if (loading)
            {
                return;
            }

            var engine = sender == LocalRadio ? TranscriptionEngine.Local
                : sender == CustomRadio ? TranscriptionEngine.CustomOpenAiCompatible
                : TranscriptionEngine.OpenAi;
            await ApplyAsync(host.Current with { Engine = engine });
            // The chosen engine's settings open below the choices; bring them into view on a short screen.
            FrameworkElement panel = engine switch
            {
                TranscriptionEngine.Local => LocalPanel,
                TranscriptionEngine.CustomOpenAiCompatible => CustomPanel,
                _ => KeyCard,
            };
            _ = Dispatcher.BeginInvoke(() => panel.BringIntoView(), DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowApplyError(host.Current, EngineCopy.Text("settings.error.applyFailed"));
        }
    }

    private async void OnAutostartClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (loading)
            {
                return;
            }

            await ApplyAsync(host.Current with { StartWithWindows = AutostartBox.IsChecked == true });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowApplyError(host.Current, EngineCopy.Text("settings.error.applyFailed"));
        }
    }

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (loading || LanguageBox.SelectedItem is not ComboBoxItem { Tag: var code })
            {
                return;
            }

            await ApplyAsync(host.Current with { Language = code as string });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowApplyError(host.Current, EngineCopy.Text("settings.error.applyFailed"));
        }
    }

    private async void OnMicrophoneChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (loading || MicrophoneBox.SelectedItem is not ComboBoxItem { Tag: var id })
            {
                return;
            }

            await ApplyAsync(host.Current with { MicrophoneId = id as string });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowApplyError(host.Current, EngineCopy.Text("settings.error.applyFailed"));
        }
    }

    private async void OnSaveKeyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var typed = KeyBox.Password;
            var error = host.SaveApiKey(typed);
            if (error is not null)
            {
                ShowKeyResult(error, Tone.Bad);
                return;
            }

            KeyBox.Clear();
            RefreshKeyState();
            ShowKeyResult("Ключ сохранен. Проверяю…", Tone.Neutral);
            if (host.Current.Engine == TranscriptionEngine.OpenAi)
            {
                // Re-apply so the running engine picks the new key up right away.
                await ApplyAsync(host.Current);
            }

            await RunCheckAsync(typed);
        }
        catch (OperationCanceledException) { ShowKeyResult(string.Empty, Tone.Neutral); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowKeyResult(EngineCopy.Text("openai.key.error.saveFailed"), Tone.Bad);
        }
    }

    private async void OnCheckKeyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var typed = KeyBox.Password;
            ShowKeyResult("Проверяю…", Tone.Neutral);
            await RunCheckAsync(string.IsNullOrWhiteSpace(typed) ? null : typed);
        }
        catch (OperationCanceledException) { ShowKeyResult(string.Empty, Tone.Neutral); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowKeyResult(EngineCopy.Text("openai.key.error.readFailed"), Tone.Bad);
        }
    }

    private async Task RunCheckAsync(string? candidate)
    {
        CheckKeyButton.IsEnabled = false;
        try
        {
            var result = await host.CheckApiKeyAsync(candidate);
            ShowKeyResult(
                result.Message,
                result.Status switch
                {
                    OpenAiKeyCheckStatus.Accepted => Tone.Good,
                    OpenAiKeyCheckStatus.Rejected => Tone.Bad,
                    _ => Tone.Warn,
                });
        }
        catch (OperationCanceledException) { ShowKeyResult(string.Empty, Tone.Neutral); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowKeyResult(EngineCopy.Text("openai.key.error.readFailed"), Tone.Bad);
        }
        finally
        {
            RefreshKeyButtons();
        }
    }

    private void OnDeleteKeyClicked(object sender, RoutedEventArgs e)
    {
        host.DeleteApiKey();
        RefreshKeyState();
        ShowKeyResult("Ключ удален.", Tone.Neutral);
    }

    // ---- Local model ----

    private void OnLocalProgressChanged(object? sender, LocalModelProgress progress) =>
        Dispatcher.BeginInvoke(() => ShowLocalProgress(progress));

    private void ShowLocalProgress(LocalModelProgress progress)
    {
        var stateChanged = progress.State != localProgress.State || progress.FailureReason != localProgress.FailureReason;
        localProgress = progress;
        if (stateChanged)
        {
            localNotice = null;
        }

        var state = progress.State;
        LocalState.Text = localNotice ?? EngineCopy.LocalState(progress, localModel.ModelSizeBytes);
        var tone = localNotice is null ? EngineCopy.LocalTone(state) : Tone.Bad;
        LocalState.Foreground = Theme.Brush(palette.HighContrast || tone == Tone.Neutral ? palette.TextMuted : ToneColour(tone));
        LocalStateDot.Fill = Theme.Brush(tone == Tone.Neutral ? palette.Accent : ToneColour(tone));

        var action = EngineCopy.LocalActionKey(state);
        if (action is null)
        {
            // While the file is checked there is nothing to press; the row keeps its height.
            LocalActionButton.Visibility = Visibility.Hidden;
        }
        else
        {
            LocalActionButton.Content = EngineCopy.Text(action);
            LocalActionButton.Style = (Style)FindResource(EngineCopy.LocalActionIsPrimary(state) ? "PrimaryButton" : "SecondaryButton");
            LocalActionButton.Visibility = Visibility.Visible;
        }

        var showsProgress = state is LocalModelState.Downloading or LocalModelState.Paused or LocalModelState.Verifying;
        LocalProgress.Visibility = Show(showsProgress);
        var total = progress.TotalBytes is > 0 ? progress.TotalBytes.Value : localModel.ModelSizeBytes;
        LocalProgress.Value = state == LocalModelState.Verifying ? 1 : Math.Clamp(progress.Bytes / (double)Math.Max(1, total), 0, 1);
        if (stateChanged)
        {
            Announce(LocalState);
        }
    }

    private void OnLocalActionClicked(object sender, RoutedEventArgs e)
    {
        switch (localProgress.State)
        {
            case LocalModelState.Downloading:
                localModel.Pause();
                break;
            case LocalModelState.Ready:
                ShowDeleteConfirmation();
                break;
            case LocalModelState.Verifying:
                break;
            default:
                localNotice = null;
                localModel.Start();
                break;
        }
    }

    private async void OnConfirmDeleteClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            HideDeleteConfirmation();
            LocalActionButton.IsEnabled = false;
            try
            {
                // Deleted: the manager reports NotInstalled through its progress event.
                var result = await localModel.DeleteAsync();
                if (result != ModelDeleteResult.Deleted)
                {
                    localNotice = EngineCopy.Text(result == ModelDeleteResult.InUse ? "local.delete.inUse" : "local.state.failed.fileAccess");
                    ShowLocalProgress(localProgress);
                    Announce(LocalState);
                }
            }
            finally
            {
                LocalActionButton.IsEnabled = true;
                LocalActionButton.Focus();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            localNotice = EngineCopy.Text("local.state.failed.fileAccess");
            ShowLocalProgress(localProgress);
            Announce(LocalState);
        }
    }

    private void HideDeleteConfirmation()
    {
        if (ConfirmLayer.Visibility != Visibility.Visible)
        {
            return;
        }

        ConfirmLayer.Visibility = Visibility.Collapsed;
        Scroller.IsEnabled = true;
        LocalActionButton.Focus();
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc answers the question instead of closing the window.
        if (e.Key == Key.Escape && ConfirmLayer.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            HideDeleteConfirmation();
        }
    }

    // ---- Own server ----

    private void InvalidateCustomCheck()
    {
        ++customCheckVersion;
        customServer.CancelCheck();
        customCheckRunning = false;
        ShowCustomResult(string.Empty, Tone.Neutral);
        RefreshCustomButtons();
    }

    private void OnCustomFieldEdited(ref bool edited)
    {
        edited = true;
        InvalidateCustomCheck();

        RefreshCustomNotes();
    }

    private void OnCustomFieldKeyDown(object sender, KeyEventArgs e)
    {
        // Enter confirms the field instead of closing the window through the default button.
        if (e.Key is Key.Enter or Key.Return)
        {
            e.Handled = true;
            _ = CommitCustomFieldsAsync();
        }
    }

    // Each valid field is saved as soon as the user leaves it; a wrong value is explained under the field and kept out of the settings.
    private async Task CommitCustomFieldsAsync()
    {
        customUrlShowsError |= customUrlEdited;
        customModelShowsError |= customModelEdited;
        RefreshCustomNotes();
        var current = host.Current;
        var url = CustomUrlBox.Text.Trim();
        var model = CustomModelBox.Text.Trim();
        var next = current with
        {
            CustomBaseUrl = EngineCopy.UrlErrorKey(url) is null ? url : current.CustomBaseUrl,
            CustomModel = EngineCopy.ModelErrorKey(model) is null ? model : current.CustomModel,
        };
        if (next == current)
        {
            return;
        }

        // Not through ApplyAsync: disabling the window here would swallow the click that moved the focus away.
        try
        {
            var error = await host.ApplyAsync(next);
            ShowApplyError(next, error);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowCustomResult(EngineCopy.Text("custom.check.failed.invalidResponse"), Tone.Bad);
        }
    }

    private void RefreshCustomNotes()
    {
        SetFieldNote(CustomUrlBox, CustomUrlNote, customUrlShowsError ? EngineCopy.UrlErrorKey(CustomUrlBox.Text) : null, "custom.url.hint");
        SetFieldNote(CustomModelBox, CustomModelNote, customModelShowsError ? EngineCopy.ModelErrorKey(CustomModelBox.Text) : null, "custom.model.hint");
        CustomKeyNote.Text = customKeyError ?? EngineCopy.Text("custom.key.hint");
        CustomKeyNote.Foreground = Theme.Brush(customKeyError is null ? palette.TextMuted : ErrorText());
    }

    private void SetFieldNote(System.Windows.Controls.TextBox box, TextBlock note, string? errorKey, string hintKey)
    {
        var wasError = box.Tag is ErrorTag;
        note.Text = EngineCopy.Text(errorKey ?? hintKey);
        note.Foreground = Theme.Brush(errorKey is null ? palette.TextMuted : ErrorText());
        box.Tag = errorKey is null ? null : ErrorTag;
        AutomationProperties.SetHelpText(box, note.Text);
        if (errorKey is not null && !wasError)
        {
            Announce(note);
        }
    }

    private void RefreshCustomKeyState()
    {
        var state = customServer.KeyState;
        var (key, tone) = state switch
        {
            CustomKeyState.Saved => ("custom.key.state.saved", Tone.Good),
            CustomKeyState.Unreadable => ("custom.key.state.unreadable", Tone.Bad),
            // The key is optional: its absence is not a warning.
            _ => ("custom.key.state.none", Tone.Neutral),
        };
        PaintChip(CustomKeyChip, CustomKeyChipDot, CustomKeyChipText, EngineCopy.Text(key), tone);
        RefreshCustomButtons();
    }

    private void RefreshCustomButtons()
    {
        CustomKeySaveButton.IsEnabled = !string.IsNullOrWhiteSpace(CustomKeyBox.Password);
        CustomKeyDeleteButton.IsEnabled = customServer.KeyState != CustomKeyState.None;
        CustomCheckButton.IsEnabled = !customCheckRunning;
    }

    private void OnCustomKeyTyped()
    {
        InvalidateCustomCheck();
        customKeyError = null;
        RefreshCustomNotes();
        RefreshCustomButtons();
    }

    private void OnCustomKeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return && CustomKeySaveButton.IsEnabled)
        {
            e.Handled = true;
            OnCustomKeySaveClicked(sender, e);
        }
    }

    private async void OnCustomKeySaveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var error = customServer.SaveKey(CustomKeyBox.Password);
            if (error is not null)
            {
                customKeyError = error;
                RefreshCustomNotes();
                Announce(CustomKeyNote);
                return;
            }

            CustomKeyBox.Clear();
            RefreshCustomKeyState();
            if (host.Current.Engine == TranscriptionEngine.CustomOpenAiCompatible)
            {
                // Re-apply so an engine that failed to start for want of the key starts now.
                var next = host.Current;
                ShowApplyError(next, await host.ApplyAsync(next));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            customKeyError = EngineCopy.Text("custom.key.error.saveFailed");
            RefreshCustomNotes();
            Announce(CustomKeyNote);
        }
    }

    private void OnCustomKeyDeleteClicked(object sender, RoutedEventArgs e)
    {
        InvalidateCustomCheck();
        customServer.DeleteKey();
        customKeyError = null;
        RefreshCustomKeyState();
        RefreshCustomNotes();
        CustomKeyBox.Focus();
    }

    private void ShowCustomResult(string text, Tone tone)
    {
        CustomCheckResult.Text = text;
        CustomCheckResult.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        CustomCheckResult.Foreground = Theme.Brush(palette.HighContrast || tone == Tone.Neutral ? palette.TextMuted : ToneColour(tone));
        if (!string.IsNullOrEmpty(text))
        {
            Announce(CustomCheckResult);
        }
    }

    // ---- Shared ----

    private async Task ApplyAsync(AppSettings next)
    {
        // Disabling the window drops the keyboard focus; remember where it was.
        var focused = FocusedControl();
        Message.Visibility = Visibility.Collapsed;
        Cursor = System.Windows.Input.Cursors.Wait;
        IsEnabled = false;
        try
        {
            ShowApplyError(next, await host.ApplyAsync(next));
        }
        finally
        {
            IsEnabled = true;
            Cursor = null;
            Reload();
            focused?.Focus();
        }
    }

    // An own server without an address or a model cannot start yet; the fields say what is missing, not a startup error.
    private void ShowApplyError(AppSettings next, string? error)
    {
        var saved = host.Current == next;
        if (saved && next.Engine == TranscriptionEngine.CustomOpenAiCompatible && !EngineCopy.IsCustomComplete(next))
        {
            error = null;
        }

        Message.Text = error ?? string.Empty;
        Message.Foreground = Theme.Brush(ErrorText());
        Message.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static System.Windows.Controls.Control? FocusedControl() =>
        Keyboard.FocusedElement switch
        {
            ComboBoxItem item => ItemsControl.ItemsControlFromItemContainer(item),
            System.Windows.Controls.Control control => control,
            _ => null,
        };

    private void ShowKeyResult(string text, Tone tone)
    {
        KeyResult.Text = text;
        KeyResult.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        KeyResult.Foreground = Theme.Brush(palette.HighContrast ? palette.Text : tone == Tone.Neutral ? palette.TextMuted : ToneColour(tone));
    }

    private Color ToneColour(Tone tone) => tone switch
    {
        Tone.Good => palette.Success,
        Tone.Warn => palette.Warning,
        Tone.Bad => palette.Error,
        _ => palette.TextMuted,
    };

    private Color ErrorText() => palette.HighContrast ? palette.Text : palette.Error;

    // Screen readers hear a new state or result without moving there.
    private static void Announce(UIElement element)
    {
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    // The window grows when an engine with more settings is chosen; it must not grow past the bottom of the screen.
    private void KeepOnScreen()
    {
        if (!IsLoaded || WindowState != WindowState.Normal)
        {
            return;
        }

        var area = SystemParameters.WorkArea;
        if (Top < area.Bottom && Top + ActualHeight > area.Bottom)
        {
            Top = Math.Max(area.Top, area.Bottom - ActualHeight);
        }
    }

    private static void Open(string url)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No default browser configured: nothing useful to do.
        }
    }

    // The title bar takes the colour of the window, so the window reads as one surface (Windows 11; older systems ignore it).
    private void MatchTitleBar()
    {
        if (palette.HighContrast)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        _ = NativeMethods.DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
        var caption = ColorRef(palette.Window);
        _ = NativeMethods.DwmSetWindowAttribute(handle, DwmCaptionColor, ref caption, sizeof(int));
        var text = ColorRef(palette.Text);
        _ = NativeMethods.DwmSetWindowAttribute(handle, DwmTextColor, ref text, sizeof(int));
        var border = ColorRef(palette.CardBorder);
        _ = NativeMethods.DwmSetWindowAttribute(handle, DwmBorderColor, ref border, sizeof(int));
    }

    private static int ColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    private static class NativeMethods
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int size);
    }
}

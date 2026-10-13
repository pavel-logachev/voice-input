using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VoiceInput.Windows.Audio;
using VoiceInput.Windows.Settings;
using VoiceInput.Windows.Transcription;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace VoiceInput.App;

/// <summary>What the settings window needs from the application. The window never touches engines or files itself.</summary>
internal interface ISettingsHost
{
    AppSettings Current { get; }

    bool HasApiKey { get; }

    /// <summary>False in ordinary installs: they ship without the local recognition worker.</summary>
    bool LocalEngineAvailable { get; }

    string Version { get; }

    /// <summary>Input devices at the moment of the call.</summary>
    IReadOnlyList<MicrophoneChoice> Microphones { get; }

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

    private static readonly (string? Code, string Label)[] Languages =
    [
        (null, "Определять автоматически"),
        ("ru", "Русский"),
        ("en", "English"),
        ("uk", "Українська"),
        ("de", "Deutsch"),
    ];

    private readonly ISettingsHost host;
    private readonly UiPalette palette = Theme.Current;
    private bool loading;

    internal SettingsWindow(ISettingsHost host)
    {
        this.host = host;
        InitializeComponent();
        Theme.Apply(this);
        HeaderIcon.Source = LoadHeaderIcon();
        SourceInitialized += (_, _) =>
        {
            UiEnvironment.ApplyTextFormatting(this);
            MatchTitleBar();
        };
        DpiChanged += (_, _) => UiEnvironment.ApplyTextFormatting(this);
        OpenAiRadio.Checked += OnEngineChanged;
        LocalRadio.Checked += OnEngineChanged;
        AutostartBox.Click += OnAutostartClicked;
        SaveKeyButton.Click += OnSaveKeyClicked;
        CheckKeyButton.Click += OnCheckKeyClicked;
        DeleteKeyButton.Click += OnDeleteKeyClicked;
        LanguageBox.SelectionChanged += OnLanguageChanged;
        MicrophoneBox.SelectionChanged += OnMicrophoneChanged;
        KeyBox.PasswordChanged += (_, _) => RefreshKeyButtons();
        KeyBox.PreviewKeyDown += OnKeyBoxKeyDown;
        SiteButton.Click += (_, _) => Open(RepositoryUrl);
        IssueButton.Click += (_, _) => Open(RepositoryUrl + "/issues/new");
        CloseButton.Click += (_, _) => Close();
        VersionText.Text = host.Version;
        // Never taller than the screen; the content scrolls on small displays.
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 24);
        if (!host.LocalEngineAvailable)
        {
            // With a single engine there is nothing to choose.
            EnginePanel.Visibility = Visibility.Collapsed;
            EngineDivider.Visibility = Visibility.Collapsed;
        }

        if (palette.HighContrast)
        {
            KeyChip.BorderThickness = new Thickness(1);
            KeyChip.BorderBrush = Theme.Brush(palette.Text);
        }

        Reload();
    }

    private enum Status
    {
        Neutral,
        Good,
        Warn,
        Bad,
    }

    internal void Reload()
    {
        loading = true;
        try
        {
            var settings = host.Current;
            var openAi = settings.Engine == TranscriptionEngine.OpenAi;
            OpenAiRadio.IsChecked = openAi;
            LocalRadio.IsChecked = settings.Engine == TranscriptionEngine.Local;
            EngineIntro.Text = openAi
                ? "Быстро и точно. Запись отправляется в OpenAI по вашему API-ключу, платит владелец ключа."
                : "Без интернета и без ключа, запись не покидает компьютер. При первом включении загружается модель.";
            AutostartBox.IsChecked = settings.StartWithWindows;
            KeyCard.Visibility = openAi ? Visibility.Visible : Visibility.Collapsed;
            FillLanguages(settings.Language);
            FillMicrophones(settings.MicrophoneId);
            RefreshKeyState();
        }
        finally
        {
            loading = false;
        }
    }

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
        var colour = has ? palette.Success : palette.Warning;
        KeyChipText.Text = has ? "Ключ сохранен" : "Ключ не задан";
        KeyChipText.Foreground = Theme.Brush(palette.HighContrast ? palette.Text : colour);
        KeyChipDot.Fill = Theme.Brush(colour);
        KeyChip.Background = Theme.Brush(palette.HighContrast ? palette.Window : Theme.WithAlpha(colour, 0x24));
        KeyState.Text = has
            ? "Звук уходит в OpenAI только в момент диктовки. Ключ зашифрован и доступен только вашей учетной записи Windows."
            : "Ключ начинается с «sk-», создать его можно на platform.openai.com. Звук уходит в OpenAI только в момент диктовки.";
        KeyPlaceholder.Text = has ? "Новый ключ — чтобы заменить" : "sk-…";
        RefreshKeyButtons();
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
        if (loading)
        {
            return;
        }

        var engine = LocalRadio.IsChecked == true ? TranscriptionEngine.Local : TranscriptionEngine.OpenAi;
        await ApplyAsync(host.Current with { Engine = engine });
    }

    private async void OnAutostartClicked(object sender, RoutedEventArgs e)
    {
        if (loading)
        {
            return;
        }

        await ApplyAsync(host.Current with { StartWithWindows = AutostartBox.IsChecked == true });
    }

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || LanguageBox.SelectedItem is not ComboBoxItem { Tag: var code })
        {
            return;
        }

        await ApplyAsync(host.Current with { Language = code as string });
    }

    private async void OnMicrophoneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || MicrophoneBox.SelectedItem is not ComboBoxItem { Tag: var id })
        {
            return;
        }

        await ApplyAsync(host.Current with { MicrophoneId = id as string });
    }

    private async void OnSaveKeyClicked(object sender, RoutedEventArgs e)
    {
        var typed = KeyBox.Password;
        var error = host.SaveApiKey(typed);
        if (error is not null)
        {
            ShowKeyResult(error, Status.Bad);
            return;
        }

        KeyBox.Clear();
        RefreshKeyState();
        ShowKeyResult("Ключ сохранен. Проверяю…", Status.Neutral);
        if (host.Current.Engine == TranscriptionEngine.OpenAi)
        {
            // Re-apply so the running engine picks the new key up right away.
            await ApplyAsync(host.Current);
        }

        await RunCheckAsync(typed);
    }

    private async void OnCheckKeyClicked(object sender, RoutedEventArgs e)
    {
        var typed = KeyBox.Password;
        ShowKeyResult("Проверяю…", Status.Neutral);
        await RunCheckAsync(string.IsNullOrWhiteSpace(typed) ? null : typed);
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
                    OpenAiKeyCheckStatus.Accepted => Status.Good,
                    OpenAiKeyCheckStatus.Rejected => Status.Bad,
                    _ => Status.Warn,
                });
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
        ShowKeyResult("Ключ удален.", Status.Neutral);
    }

    private async Task ApplyAsync(AppSettings next)
    {
        // Disabling the window drops the keyboard focus; remember where it was.
        var focused = FocusedControl();
        Message.Visibility = Visibility.Collapsed;
        Cursor = System.Windows.Input.Cursors.Wait;
        IsEnabled = false;
        try
        {
            var error = await host.ApplyAsync(next);
            Message.Text = error ?? string.Empty;
            Message.Foreground = Theme.Brush(palette.HighContrast ? palette.Text : palette.Error);
            Message.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        }
        finally
        {
            IsEnabled = true;
            Cursor = null;
            Reload();
            focused?.Focus();
        }
    }

    private static System.Windows.Controls.Control? FocusedControl() =>
        Keyboard.FocusedElement switch
        {
            ComboBoxItem item => ItemsControl.ItemsControlFromItemContainer(item),
            System.Windows.Controls.Control control => control,
            _ => null,
        };

    private void ShowKeyResult(string text, Status status)
    {
        KeyResult.Text = text;
        KeyResult.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        KeyResult.Foreground = Theme.Brush(status switch
        {
            _ when palette.HighContrast => palette.Text,
            Status.Good => palette.Success,
            Status.Warn => palette.Warning,
            Status.Bad => palette.Error,
            _ => palette.TextMuted,
        });
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

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using VoiceInput.Windows.Audio;
using VoiceInput.Windows.Settings;
using VoiceInput.Windows.Transcription;
using Color = System.Windows.Media.Color;

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
    private bool loading;

    internal SettingsWindow(ISettingsHost host)
    {
        this.host = host;
        InitializeComponent();
        SourceInitialized += (_, _) => UseDarkTitleBar();
        OpenAiRadio.Checked += OnEngineChanged;
        LocalRadio.Checked += OnEngineChanged;
        AutostartBox.Click += OnAutostartClicked;
        SaveKeyButton.Click += OnSaveKeyClicked;
        CheckKeyButton.Click += OnCheckKeyClicked;
        DeleteKeyButton.Click += OnDeleteKeyClicked;
        LanguageBox.SelectionChanged += OnLanguageChanged;
        MicrophoneBox.SelectionChanged += OnMicrophoneChanged;
        KeyBox.PasswordChanged += (_, _) => RefreshKeyButtons();
        SiteButton.Click += (_, _) => Open(RepositoryUrl);
        IssueButton.Click += (_, _) => Open(RepositoryUrl + "/issues/new");
        CloseButton.Click += (_, _) => Close();
        VersionText.Text = $"v{host.Version}";
        // Never taller than the screen; the content scrolls on small displays.
        MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 48);
        ConfigureEngineChoice();
        Reload();
    }

    internal void Reload()
    {
        loading = true;
        try
        {
            var settings = host.Current;
            OpenAiRadio.IsChecked = settings.Engine == TranscriptionEngine.OpenAi;
            LocalRadio.IsChecked = settings.Engine == TranscriptionEngine.Local;
            AutostartBox.IsChecked = settings.StartWithWindows;
            KeyCard.Visibility = settings.Engine == TranscriptionEngine.OpenAi ? Visibility.Visible : Visibility.Collapsed;
            FillLanguages(settings.Language);
            FillMicrophones(settings.MicrophoneId);
            RefreshKeyState();
        }
        finally
        {
            loading = false;
        }
    }

    // With a single engine there is nothing to choose: show a plain statement instead of a one-item radio list.
    private void ConfigureEngineChoice()
    {
        if (host.LocalEngineAvailable)
        {
            return;
        }

        LocalRadio.Visibility = Visibility.Collapsed;
        OpenAiRadio.Visibility = Visibility.Collapsed;
        EngineIntro.Text = "Речь превращается в текст через OpenAI по вашему API-ключу. Звук уходит в OpenAI только в момент диктовки.";
        EngineIntro.Margin = new Thickness(0, 3, 0, 12);
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
            var item = new ComboBoxItem { Content = microphone.Name, Tag = microphone.Id };
            MicrophoneBox.Items.Add(item);
            if (string.Equals(microphone.Id, selectedId, StringComparison.Ordinal))
            {
                selected = item;
            }
        }

        // A saved microphone that is unplugged right now: keep the choice visible, Windows default is used meanwhile.
        if (selected is null)
        {
            selected = new ComboBoxItem { Content = "Выбранный микрофон не подключён · используется микрофон Windows", Tag = selectedId };
            MicrophoneBox.Items.Add(selected);
        }

        MicrophoneBox.SelectedItem = selected;
    }

    private void RefreshKeyState()
    {
        var has = host.HasApiKey;
        KeyChipText.Text = has ? "Ключ сохранён" : "Ключ не задан";
        var good = has;
        KeyChip.Background = new SolidColorBrush(good ? Color.FromRgb(0x14, 0x30, 0x25) : Color.FromRgb(0x3A, 0x2D, 0x12));
        KeyChipDot.Fill = new SolidColorBrush(good ? Color.FromRgb(0x69, 0xD6, 0xA4) : Color.FromRgb(0xF2, 0xC6, 0x6D));
        KeyChipText.Foreground = new SolidColorBrush(good ? Color.FromRgb(0x9D, 0xE6, 0xC4) : Color.FromRgb(0xF2, 0xD8, 0xA0));
        KeyState.Text = has
            ? "Чтобы заменить ключ, вставьте новый и нажмите «Сохранить»."
            : "Он нужен для распознавания через OpenAI и начинается с «sk-». Создать ключ можно на platform.openai.com.";
        RefreshKeyButtons();
    }

    private void RefreshKeyButtons()
    {
        var typed = !string.IsNullOrWhiteSpace(KeyBox.Password);
        SaveKeyButton.IsEnabled = typed;
        CheckKeyButton.IsEnabled = typed || host.HasApiKey;
        DeleteKeyButton.IsEnabled = host.HasApiKey;
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
        ShowKeyResult("Ключ сохранён. Проверяю…", Status.Neutral);
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
        ShowKeyResult("Ключ удалён.", Status.Neutral);
    }

    private async Task ApplyAsync(AppSettings next)
    {
        Message.Text = "Применяю…";
        IsEnabled = false;
        try
        {
            var error = await host.ApplyAsync(next);
            Message.Text = error ?? string.Empty;
        }
        finally
        {
            IsEnabled = true;
            Reload();
        }
    }

    private enum Status
    {
        Neutral,
        Good,
        Warn,
        Bad,
    }

    private void ShowKeyResult(string text, Status status)
    {
        KeyResult.Text = text;
        KeyResult.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        KeyResult.Foreground = new SolidColorBrush(status switch
        {
            Status.Good => Color.FromRgb(0x69, 0xD6, 0xA4),
            Status.Warn => Color.FromRgb(0xF2, 0xC6, 0x6D),
            Status.Bad => Color.FromRgb(0xFF, 0x6B, 0x83),
            _ => Color.FromRgb(0x9E, 0xA3, 0xB3),
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

    private void UseDarkTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        _ = NativeMethods.DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    private static class NativeMethods
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int size);
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using VoiceInput.Windows.Settings;

namespace VoiceInput.App;

/// <summary>What the settings window needs from the application. The window never touches engines or files itself.</summary>
internal interface ISettingsHost
{
    AppSettings Current { get; }

    bool HasApiKey { get; }

    /// <summary>False in ordinary installs: they ship without the local recognition worker.</summary>
    bool LocalEngineAvailable { get; }

    string Version { get; }

    /// <summary>Applies new settings; returns a user-facing error message, or null on success.</summary>
    Task<string?> ApplyAsync(AppSettings next);

    /// <summary>Stores the API key; returns a user-facing error message, or null on success.</summary>
    string? SaveApiKey(string apiKey);

    void DeleteApiKey();
}

public partial class SettingsWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;

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
        DeleteKeyButton.Click += OnDeleteKeyClicked;
        CloseButton.Click += (_, _) => Close();
        VersionText.Text = $"Voice Input {host.Version}";
        ConfigureEngineChoice();
        Reload();
    }

    // With a single engine there is nothing to choose: show a plain statement instead of a one-item radio list.
    private void ConfigureEngineChoice()
    {
        if (host.LocalEngineAvailable)
        {
            return;
        }

        LocalRadio.Visibility = Visibility.Collapsed;
        EngineIntro.Text = "Речь превращается в текст через OpenAI. Нужен ваш API-ключ.";
        OpenAiRadio.IsHitTestVisible = false;
        OpenAiRadio.Focusable = false;
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
            RefreshKeyState();
        }
        finally
        {
            loading = false;
        }
    }

    private void RefreshKeyState()
    {
        var has = host.HasApiKey;
        KeyState.Text = has
            ? "Ключ сохранён. Чтобы заменить его, вставьте новый и нажмите «Сохранить»."
            : "Ключ не задан. Он нужен только для распознавания через OpenAI: начинается с «sk-».";
        DeleteKeyButton.IsEnabled = has;
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

    private async void OnSaveKeyClicked(object sender, RoutedEventArgs e)
    {
        var error = host.SaveApiKey(KeyBox.Password);
        if (error is not null)
        {
            Message.Text = error;
            return;
        }

        KeyBox.Clear();
        RefreshKeyState();
        Message.Text = "Ключ сохранён.";
        if (host.Current.Engine == TranscriptionEngine.OpenAi)
        {
            // Re-apply so the running engine picks the new key up right away.
            await ApplyAsync(host.Current);
        }
    }

    private void OnDeleteKeyClicked(object sender, RoutedEventArgs e)
    {
        host.DeleteApiKey();
        RefreshKeyState();
        Message.Text = "Ключ удалён.";
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

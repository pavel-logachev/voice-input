using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VoiceInput.App;
using VoiceInput.Core.Activation;
using VoiceInput.Windows.Settings;

namespace VoiceInput.UiPreview;

/// <summary>
/// Renders the real overlay and settings windows to PNG files so the design can be reviewed
/// (and used in documentation) without recording a dictation. Usage: UiPreview &lt;output-directory&gt;.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "preview");
        Directory.CreateDirectory(output);

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var written = new List<string>();

        var overlay = new MainWindow { Left = -10_000, Top = -10_000 };
        overlay.Show(ActivationVisualState.Listening);
        for (var index = 0; index < 40; index++)
        {
            overlay.SetRecordingLevel((float)Math.Abs(Math.Sin(index / 3.2)) * (0.15f + (index / 55f)));
        }

        written.Add(Capture(overlay, output, "overlay-listening"));

        overlay.Show(ActivationVisualState.Processing);
        written.Add(Capture(overlay, output, "overlay-processing"));
        overlay.Show(ActivationVisualState.Inserting);
        written.Add(Capture(overlay, output, "overlay-inserting"));
        overlay.Show(ActivationVisualState.Success);
        written.Add(Capture(overlay, output, "overlay-success"));
        overlay.Show(ActivationVisualState.NoSpeech);
        written.Add(Capture(overlay, output, "overlay-nospeech"));
        overlay.ShowError("OpenAI отклонил API-ключ (HTTP 401).");
        written.Add(Capture(overlay, output, "overlay-error"));
        overlay.ShowStatus("Окно сменилось", "Текст не вставлен");
        written.Add(Capture(overlay, output, "overlay-status"));

        var withoutKey = new SettingsWindow(new FakeHost(new AppSettings(), hasKey: false)) { Left = -10_000, Top = -10_000 };
        withoutKey.Show();
        written.Add(Capture(withoutKey, output, "settings-no-key"));
        withoutKey.Close();

        var local = new SettingsWindow(new FakeHost(new AppSettings { StartWithWindows = true }, hasKey: true))
        {
            Left = -10_000,
            Top = -10_000,
        };
        local.Show();
        written.Add(Capture(local, output, "settings-ready"));
        local.Close();

        overlay.CloseForShutdown();
        foreach (var path in written)
        {
            Console.WriteLine(path);
        }

        application.Shutdown();
        return 0;
    }

    private static string Capture(Window window, string directory, string name)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        var visual = (Visual)VisualTreeHelper.GetChild(window, 0);
        var margin = window is MainWindow ? 24 : 0;
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        const double dpi = 192;
        var scale = dpi / 96;

        var bitmap = new RenderTargetBitmap(
            (int)((width + (2 * margin)) * scale),
            (int)((height + (2 * margin)) * scale),
            dpi,
            dpi,
            PixelFormats.Pbgra32);

        // Dark backdrop so the translucent card reads like it does over a real desktop.
        var canvas = new DrawingVisual();
        using (var context = canvas.RenderOpen())
        {
            context.DrawRectangle(
                new SolidColorBrush(window is MainWindow ? Color.FromRgb(31, 33, 44) : Color.FromRgb(14, 16, 22)),
                null,
                new Rect(0, 0, width + (2 * margin), height + (2 * margin)));
            context.DrawRectangle(new VisualBrush(visual), null, new Rect(margin, margin, width, height));
        }

        bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(directory, name + ".png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private sealed class FakeHost(AppSettings settings, bool hasKey) : ISettingsHost
    {
        public AppSettings Current { get; } = settings;

        public bool HasApiKey { get; } = hasKey;

        public bool LocalEngineAvailable => false;

        public string Version => "1.0.0";

        public Task<string?> ApplyAsync(AppSettings next) => Task.FromResult<string?>(null);

        public string? SaveApiKey(string apiKey) => null;

        public void DeleteApiKey()
        {
        }
    }
}

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VoiceInput.App;
using VoiceInput.Core.Activation;
using VoiceInput.Windows.Audio;
using VoiceInput.Windows.Settings;
using VoiceInput.Windows.Transcription;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using Forms = System.Windows.Forms;

namespace VoiceInput.UiPreview;

/// <summary>
/// Renders the real overlay, settings window and tray menu to PNG files so the design can be reviewed
/// (and used in documentation) without recording a dictation.
/// Usage: UiPreview &lt;output-directory&gt; [--dpi=96|144|192|384] [--high-contrast] [--transparent] [--live-download].
/// --transparent leaves the overlay pictures without a backdrop, for laying them over other artwork.
/// --live-download also runs the real model download against a small file on a local test server.
/// </summary>
internal static class Program
{
    private const string LongError =
        "Не удалось связаться с OpenAI: проверьте подключение к интернету и повторите диктовку через несколько секунд.";

    private static double dpi = 192;
    private static bool transparentOverlay;

    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? "preview");
        foreach (var arg in args)
        {
            if (arg.StartsWith("--dpi=", StringComparison.Ordinal))
            {
                dpi = double.Parse(arg["--dpi=".Length..], CultureInfo.InvariantCulture);
            }
            else if (arg == "--high-contrast")
            {
                UiEnvironment.HighContrastOverride = true;
            }
            else if (arg == "--transparent")
            {
                transparentOverlay = true;
            }
        }

        // A still frame: no motion, and text shaped the way the requested scale shapes it.
        UiEnvironment.AnimationsOverride = false;
        UiEnvironment.ScaleOverride = dpi / 96;
        Directory.CreateDirectory(output);

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var written = new List<string>();

        var overlay = new MainWindow { Left = -10_000, Top = -10_000 };
        overlay.Show(ActivationVisualState.Listening);
        overlay.ShowElapsed(TimeSpan.FromSeconds(7));
        for (var index = 0; index < 60; index++)
        {
            overlay.AudioMeter.AdvancePreview(
                (float)Math.Abs(Math.Sin(index / 3.2)) * (0.15f + (index / 82f)), TimeSpan.FromMilliseconds(30));
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
        overlay.ShowError(LongError);
        written.Add(Capture(overlay, output, "overlay-error-long"));
        overlay.ShowStatus("Окно сменилось", "Текст не вставлен");
        written.Add(Capture(overlay, output, "overlay-status"));
        overlay.ShowStatus("Voice Input запускается", "Повторите через несколько секунд");
        written.Add(Capture(overlay, output, "overlay-status-long"));

        var withoutKey = new SettingsWindow(new FakeHost(new AppSettings(), hasKey: false)) { Left = -10_000, Top = -10_000 };
        withoutKey.Show();
        written.Add(Capture(withoutKey, output, "settings-no-key"));
        withoutKey.Close();

        var ready = new SettingsWindow(new FakeHost(new AppSettings { StartWithWindows = true, Language = "ru", MicrophoneId = "b" }, hasKey: true))
        {
            Left = -10_000,
            Top = -10_000,
        };
        ready.Show();
        written.Add(Capture(ready, output, "settings-ready"));
        written.Add(CaptureDropDown(ready, ready.MicrophoneBox, output, "settings-microphone-list"));
        written.AddRange(CaptureKeyboardFocus(ready, output));
        ready.Close();

        var engines = new SettingsWindow(new FakeHost(new AppSettings { Language = "ru", MicrophoneId = "missing" }, hasKey: true, localEngine: true))
        {
            Left = -10_000,
            Top = -10_000,
        };
        engines.Show();
        written.Add(Capture(engines, output, "settings-engines"));
        engines.Close();

        written.AddRange(EnginePreview.CaptureAll(output));
        if (args.Contains("--live-download"))
        {
            written.AddRange(EnginePreview.CaptureLiveDownload(output));
        }

        written.AddRange(CaptureTrayMenu(output));

        overlay.CloseForShutdown();
        foreach (var path in written)
        {
            Console.WriteLine(path);
        }

        application.Shutdown();
        return 0;
    }

    private static IEnumerable<string> CaptureTrayMenu(string directory)
    {
        using var menu = new TrayMenu(localEngineAvailable: false, scaleOverride: (float)(dpi / 96));
        menu.SetStatus("Готов · OpenAI", TrayStatus.Ready);
        menu.FillMicrophones(FakeHost.Devices, "b", _ => { });
        yield return CaptureStrip(menu.Strip, directory, "tray-menu");
        yield return CaptureStrip(menu.Microphone.DropDown, directory, "tray-menu-microphone");
        yield return CaptureStrip(menu.Hotkeys.DropDown, directory, "tray-menu-hotkeys");
    }

    private static string CaptureStrip(Forms.ToolStripDropDown strip, string directory, string name)
    {
        strip.CreateControl();
        strip.PerformLayout();
        var size = strip.GetPreferredSize(System.Drawing.Size.Empty);
        strip.Size = size;
        using var menu = new System.Drawing.Bitmap(size.Width, size.Height);
        strip.DrawToBitmap(menu, new System.Drawing.Rectangle(System.Drawing.Point.Empty, size));

        // Windows 11 rounds the corners of the menu window and outlines it; a control cannot draw that itself,
        // so the picture repeats what the system adds. In high contrast the menu stays a plain system rectangle.
        var scale = (float)(dpi / 96);
        var margin = (int)(16 * scale);
        var palette = Theme.Current;
        using var picture = new System.Drawing.Bitmap(size.Width + (2 * margin), size.Height + (2 * margin));
        using (var graphics = System.Drawing.Graphics.FromImage(picture))
        {
            graphics.Clear(System.Drawing.Color.FromArgb(31, 33, 44));
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var bounds = new System.Drawing.RectangleF(margin, margin, size.Width, size.Height);
            if (palette.HighContrast)
            {
                graphics.DrawImage(menu, margin, margin);
            }
            else
            {
                using var outline = Rounded(bounds, 8 * scale);
                using var texture = new System.Drawing.TextureBrush(menu);
                texture.TranslateTransform(margin, margin);
                graphics.FillPath(texture, outline);
                using var pen = new System.Drawing.Pen(Theme.ToDrawing(palette.MenuBorder), scale);
                graphics.DrawPath(pen, outline);
            }
        }

        var path = Path.Combine(directory, name + ".png");
        picture.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    private static System.Drawing.Drawing2D.GraphicsPath Rounded(System.Drawing.RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void Pump(Window window, TimeSpan duration)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(
            duration,
            System.Windows.Threading.DispatcherPriority.Normal,
            (_, _) => frame.Continue = false,
            window.Dispatcher);
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    // The open list of a drop-down lives in its own popup window, outside the settings window's picture.
    private static string CaptureDropDown(Window window, System.Windows.Controls.ComboBox box, string directory, string name)
    {
        box.IsDropDownOpen = true;
        Pump(window, TimeSpan.FromMilliseconds(250));
        var popup = (System.Windows.Controls.Primitives.Popup)box.Template.FindName("PART_Popup", box);
        Visual root = popup.Child;
        while (VisualTreeHelper.GetParent(root) is Visual parent)
        {
            root = parent;
        }

        var path = Render(window, (FrameworkElement)root, Color.FromRgb(14, 16, 22), directory, name);
        box.IsDropDownOpen = false;
        return path;
    }

    // Moves the keyboard focus through the window and shows where the focus ring lands.
    private static IEnumerable<string> CaptureKeyboardFocus(SettingsWindow window, string directory)
    {
        // WPF draws the ring only after real keyboard input, which a picture tool does not have.
        // The switch is internal to WPF; if a future version drops it, the pictures simply come out without a ring.
        typeof(System.Windows.Input.KeyboardNavigation)
            .GetProperty("AlwaysShowFocusVisual", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(null, true);
        window.Activate();
        var stops = new (string Name, System.Windows.Controls.Control Control)[]
        {
            ("list", window.MicrophoneBox),
            ("field", window.KeyBox),
            ("button", window.CheckKeyButton),
            ("switch", window.AutostartBox),
            ("link", window.IssueButton),
        };
        foreach (var (name, control) in stops)
        {
            control.Focus();
            yield return Capture(window, directory, "settings-focus-" + name);
        }
    }

    internal static string CaptureWindow(Window window, string directory, string name) => Capture(window, directory, name);

    internal static void PumpFor(Window window, TimeSpan duration) => Pump(window, duration);

    private static string Capture(Window window, string directory, string name)
    {
        window.UpdateLayout();
        // Let the layout and the first frame settle before the picture is taken.
        Pump(window, TimeSpan.FromMilliseconds(120));
        var visual = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        return Render(
            window,
            visual,
            window is MainWindow
                ? (transparentOverlay ? Colors.Transparent : Color.FromRgb(31, 33, 44))
                : Color.FromRgb(14, 16, 22),
            directory,
            name);
    }

    private static string Render(Window window, FrameworkElement visual, Color backdrop, string directory, string name)
    {
        // Measure the rendered content, not the window: a framed window's size also counts its title bar and borders.
        var width = (int)Math.Ceiling(visual.ActualWidth);
        var height = (int)Math.Ceiling(visual.ActualHeight);
        var scale = dpi / 96;

        var bitmap = new RenderTargetBitmap(
            (int)(width * scale),
            (int)(height * scale),
            dpi,
            dpi,
            PixelFormats.Pbgra32);

        // The overlay window is transparent around the capsule: paint a desktop-like backdrop under it.
        var canvas = new DrawingVisual();
        using (var context = canvas.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(backdrop), null, new Rect(0, 0, width, height));
            // Map the window rectangle 1:1. A default VisualBrush stretches the visual's content bounds —
            // which include the drop shadow — to fill the target, and that squeezes the overlay out of proportion.
            var brush = new VisualBrush(visual)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, width, height),
            };
            context.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }

        bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(directory, name + ".png");
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    internal sealed class FakeHost(
        AppSettings settings,
        bool hasKey,
        bool localEngine = true,
        ILocalModelHost? localModel = null,
        ICustomServerHost? customServer = null) : ISettingsHost
    {
        public static IReadOnlyList<MicrophoneChoice> Devices { get; } =
        [
            new("a", "Микрофон (Realtek High Definition Audio)"),
            new("b", "Гарнитура (Logi Zone Wired)"),
        ];

        public AppSettings Current { get; private set; } = settings;

        public bool HasApiKey { get; } = hasKey;

        public bool LocalEngineAvailable { get; } = localEngine;

        public IReadOnlyList<MicrophoneChoice> Microphones => Devices;

        public ILocalModelHost LocalModel { get; } = localModel ?? new EnginePreview.FakeLocalModel();

        public ICustomServerHost CustomServer { get; } = customServer ?? new EnginePreview.FakeCustomServer();

        public Task<OpenAiKeyCheckResult> CheckApiKeyAsync(string? candidate) =>
            Task.FromResult(new OpenAiKeyCheckResult(OpenAiKeyCheckStatus.Accepted, "Ключ работает."));

        // The version the application itself is built with, so the pictures never show a stale number.
        public string Version { get; } = typeof(MainWindow).Assembly.GetName().Version!.ToString(3);

        public Task<string?> ApplyAsync(AppSettings next)
        {
            Current = next;
            return Task.FromResult<string?>(null);
        }

        public string? SaveApiKey(string apiKey) => null;

        public void DeleteApiKey()
        {
        }
    }
}

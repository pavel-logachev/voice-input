using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VoiceInput.App;
using VoiceInput.Core.Activation;
using Application = System.Windows.Application;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

// Drives only a fresh MainWindow: no App bootstrap, hotkeys, microphone, tray or installed process.
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("VOICE_INPUT_DATA_DIR", Path.Combine(output, "data"));
        Environment.SetEnvironmentVariable("VOICE_INPUT_INSTANCE_NAME", "Local\\VoiceInput.OverlayHarness");
        if (args.Any(arg => arg.StartsWith("--crash=", StringComparison.Ordinal)))
            return ExerciseCrashHandler(args.First(arg => arg.StartsWith("--crash=", StringComparison.Ordinal))[8..]);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow();
        var initialHeight = window.ActualHeight;
        window.Show(ActivationVisualState.Listening);
        window.UpdateLayout();
        var capsule = (FrameworkElement)window.FindName("Frame");
        var timer = (TextBlock)window.FindName("TimerText");
        var meter = (FrameworkElement)window.FindName("AudioMeter");
        var screen = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(window).Handle);
        var scale = VisualTreeHelper.GetDpi(window);
        var firstBottom = window.PointToScreen(capsule.TranslatePoint(new Point(0, capsule.ActualHeight), window)).Y;
        var firstInside = firstBottom <= screen.WorkingArea.Bottom;
        var intervals = new List<double>();
        var previous = TimeSpan.Zero;
        EventHandler frameHandler = (_, e) =>
        {
            var time = ((RenderingEventArgs)e).RenderingTime;
            if (time == previous) return;
            if (previous != TimeSpan.Zero) intervals.Add((time - previous).TotalMilliseconds);
            previous = time;
        };
        CompositionTarget.Rendering += frameHandler;
        var producerThread = 0;
        long audioPosts = 0;
        window.Dispatcher.Hooks.OperationPosted += (_, _) =>
        {
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref producerThread)) Interlocked.Increment(ref audioPosts);
        };
        using var stop = new CancellationTokenSource();
        var producer = Task.Run(() =>
        {
            Volatile.Write(ref producerThread, Environment.CurrentManagedThreadId);
            var clock = Stopwatch.StartNew();
            var count = 0;
            while (!stop.IsCancellationRequested)
            {
                window.SetRecordingLevel((float)(0.45 + 0.4 * Math.Sin(clock.Elapsed.TotalSeconds * 8)));
                count++;
                Thread.Sleep(count % 5 == 0 ? 55 : 5); // Deliberately irregular audio buffers, ~65 events/s.
            }
        });
        var duration = Stopwatch.StartNew();
        Pump(TimeSpan.FromSeconds(5));
        stop.Cancel();
        producer.GetAwaiter().GetResult();
        duration.Stop();
        CompositionTarget.Rendering -= frameHandler;
        Capture(window, Path.Combine(output, "recording.png"));
        var renderTimes = new List<double>();
        for (var i = 0; i < 100; i++)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(meter.ActualWidth), 18, 96, 96, PixelFormats.Pbgra32);
            var clock = Stopwatch.StartNew();
            bitmap.Render(meter);
            renderTimes.Add(clock.Elapsed.TotalMilliseconds);
        }
        var original = Geometry(window, meter, capsule);
        var timerGeometry = new Dictionary<string, double[]>();
        foreach (var text in new[] { "0:09", "0:10", "1:00", "9:59", "10:00" })
        {
            timer.Text = text;
            window.UpdateLayout();
            timerGeometry[text] = Geometry(window, meter, capsule);
        }
        window.Hide();
        Pump(TimeSpan.FromMilliseconds(100)); // drain the final hide frame before measuring idle
        var hiddenOperations = 0;
        System.Windows.Threading.DispatcherHookEventHandler hiddenHook = (_, _) => hiddenOperations++;
        window.Dispatcher.Hooks.OperationPosted += hiddenHook;
        for (var i = 0; i < 100; i++) window.SetRecordingLevel(1);
        Pump(TimeSpan.FromMilliseconds(400));
        window.Dispatcher.Hooks.OperationPosted -= hiddenHook;
        var repeatInside = true;
        for (var i = 0; i < 3; i++)
        {
            window.Show(ActivationVisualState.Listening);
            Pump(TimeSpan.FromMilliseconds(250));
            repeatInside &= CapsuleInside(window, capsule, screen.WorkingArea.Bottom);
            ((IActivationOverlay)window).Hide();
            Pump(TimeSpan.FromMilliseconds(250));
        }
        window.ShowError("Synthetic long diagnostic message repeated to exercise a taller overlay without recording speech.");
        Pump(TimeSpan.FromMilliseconds(250));
        var resizedInside = CapsuleInside(window, capsule, screen.WorkingArea.Bottom);
        window.Hide();
        var result = new
        {
            InitialActualHeight = initialHeight,
            Window = new { window.Left, window.Top, window.ActualWidth, window.ActualHeight },
            Dpi = scale.PixelsPerInchX,
            WorkArea = new { screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Right, screen.WorkingArea.Bottom },
            FirstCapsuleBottomPixels = firstBottom,
            FirstShowInsideWorkArea = firstInside,
            Frames = intervals.Count,
            FrameMeanMs = intervals.Average(),
            FrameStdDevMs = Math.Sqrt(intervals.Average(x => Math.Pow(x - intervals.Average(), 2))),
            FrameP95Ms = Percentile(intervals, 0.95),
            AudioDispatcherPostsPerSecond = audioPosts / duration.Elapsed.TotalSeconds,
            MeterSoftwareSnapshotMeanMs = renderTimes.Average(),
            MeterSoftwareSnapshotP95Ms = Percentile(renderTimes, 0.95),
            InitialGeometry = original,
            TimerGeometry = timerGeometry,
            HiddenDispatcherOperations = hiddenOperations,
            RepeatedShowsInsideWorkArea = repeatInside,
            TallerErrorInsideWorkArea = resizedInside,
        };
        File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(result, JsonOptions));
        app.Shutdown();
        return args.Contains("--assert") && (!firstInside || !repeatInside || !resizedInside || audioPosts != 0) ? 1 : 0;
    }

    private static int ExerciseCrashHandler(string kind)
    {
        // Do not Run App: its constructor registers logging, but startup would acquire hotkeys/tray.
        var app = new VoiceInput.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (kind == "domain")
        {
            var thread = new Thread(() => throw new InvalidOperationException("SECRET synthetic payload"));
            thread.Start();
            thread.Join();
        }
        else if (kind == "dispatcher")
        {
            app.Dispatcher.BeginInvoke(() => throw new InvalidOperationException("SECRET synthetic payload"));
            Pump(TimeSpan.FromMilliseconds(300));
        }
        else if (kind == "task")
        {
            CreateUnobservedTask();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        return 0;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void CreateUnobservedTask()
    {
        var task = Task.Run(() => throw new InvalidOperationException("SECRET synthetic payload"));
        ((IAsyncResult)task).AsyncWaitHandle.WaitOne(); // wait without observing Exception
    }

    private static bool CapsuleInside(Window window, FrameworkElement capsule, double bottom) =>
        window.PointToScreen(capsule.TranslatePoint(new Point(0, capsule.ActualHeight), window)).Y <= bottom;

    private static double[] Geometry(Window window, FrameworkElement meter, FrameworkElement capsule) =>
        [capsule.ActualWidth, capsule.ActualHeight, meter.TranslatePoint(new Point(), window).X, meter.ActualWidth];

    private static double Percentile(List<double> values, double quantile) => values.Order().ElementAt((int)((values.Count - 1) * quantile));

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void Capture(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 2), (int)Math.Ceiling(window.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        var canvas = new DrawingVisual();
        using (var context = canvas.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(31, 33, 44)), null, new Rect(new Size(window.ActualWidth, window.ActualHeight)));
            context.DrawRectangle(new VisualBrush(window), null, new Rect(new Size(window.ActualWidth, window.ActualHeight)));
        }
        bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}

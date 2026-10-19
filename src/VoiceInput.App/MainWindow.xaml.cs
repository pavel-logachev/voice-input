using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VoiceInput.Core.Activation;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace VoiceInput.App;

public partial class MainWindow : Window, IActivationOverlay
{
    private const int ExtendedStyleIndex = -20;
    private const int NoActivateStyle = 0x08000000;
    private const int ToolWindowStyle = 0x00000080;
    private const int ClickThroughStyle = 0x00000020;
    private const double ActivityWidth = 320;
    private const double MaximumWidth = 360;

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly UiPalette palette = Theme.Current;
    private DateTime listeningSince;
    private int hideVersion;
    private bool shutdownRequested;
    private bool closed;
    private nint targetMonitor;
    private bool positioning;

    public MainWindow()
    {
        InitializeComponent();
        Theme.Apply(this);
        ApplySurface();
        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) =>
        {
            UiEnvironment.ApplyTextFormatting(this);
            if (IsVisible) PositionAboveTaskbar();
        };
        SizeChanged += (_, _) => { if (IsVisible) PositionAboveTaskbar(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            StopMotion();
            StopPresentationMotion();
        };
        Closed += (_, _) =>
        {
            closed = true;
            StopMotion();
            StopPresentationMotion();
        };
        timer.Tick += (_, _) => UpdateTimerText();
    }

    // The shape of the mark that stands for a stage or an outcome.
    private enum Mark
    {
        Record,
        Spinner,
        Caret,
        Check,
        Silence,
        Error,
        Note,
    }

    internal void CloseForShutdown()
    {
        shutdownRequested = true;
        if (!closed)
        {
            Close();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!shutdownRequested && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    public void Show(ActivationVisualState state)
    {
        StopMotion();
        DetailText.Visibility = Visibility.Collapsed;
        CompactOverlayActivity? activity = null;
        if (state is ActivationVisualState.Listening or ActivationVisualState.Processing)
        {
            var presentation = CompactOverlayPresentation.For(state);
            StatusText.Text = presentation.Title;
            activity = presentation.Activity;
        }
        else
        {
            StatusText.Text = state switch
            {
                ActivationVisualState.NoSpeech => "Не услышал",
                ActivationVisualState.Inserting => "Вставляю текст",
                ActivationVisualState.Success => "Готово",
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
            };
        }

        // One colour and one shape per stage: recording, recognising, typing, done, nothing heard.
        var (mark, accent) = state switch
        {
            ActivationVisualState.Listening => (Mark.Record, palette.Record),
            ActivationVisualState.Processing => (Mark.Spinner, palette.Process),
            ActivationVisualState.Inserting => (Mark.Caret, palette.Insert),
            ActivationVisualState.Success => (Mark.Check, palette.Success),
            _ => (Mark.Silence, palette.Warning),
        };
        SetActivity(activity, accent);
        SetMark(mark, accent);
        Present(animateWidth: true);
        StartMotion(state);
    }

    public void ShowError(string message)
    {
        StopMotion();
        StatusText.Text = "Не удалось";
        DetailText.Text = message;
        DetailText.Visibility = Visibility.Visible;
        SetActivity(null, palette.Error);
        SetMark(Mark.Error, palette.Error);
        Present(animateWidth: false);
    }

    public void ShowStatus(string title, string detail)
    {
        StopMotion();
        StatusText.Text = title;
        DetailText.Text = detail;
        DetailText.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
        SetActivity(null, palette.Neutral);
        SetMark(Mark.Note, palette.Neutral);
        Present(animateWidth: false);
    }

    public void SetRecordingLevel(float level) => AudioMeter.SetLevel(level);

    void IActivationOverlay.Hide()
    {
        if (!IsVisible || closed)
        {
            return;
        }

        if (!UiEnvironment.Animations)
        {
            StopMotion();
            Hide();
            return;
        }

        var version = ++hideVersion;
        var fadeOut = (Storyboard)Resources["FadeOutStoryboard"];
        EventHandler? completed = null;
        completed = (_, _) =>
        {
            fadeOut.Completed -= completed;
            if (version == hideVersion && !closed)
            {
                StopMotion();
                Hide();
            }
        };
        fadeOut.Completed += completed;
        fadeOut.Begin(this, true);
    }

    private void ApplySurface()
    {
        AudioMeter.SetAppearance(palette.Wave, palette.OverlayTrack);
        if (palette.HighContrast)
        {
            // No shadow in high contrast: a solid outline in the text colour separates the capsule instead.
            Shadow.Visibility = Visibility.Collapsed;
            Frame.BorderThickness = new Thickness(2);
        }
        else
        {
            Shadow.Background = Theme.Brush(Theme.WithAlpha(palette.OverlaySurface, 255));
        }
    }

    private void SetActivity(CompactOverlayActivity? activity, Color accent)
    {
        ActivityHost.Visibility = activity.HasValue ? Visibility.Visible : Visibility.Collapsed;
        AudioMeter.Visibility = activity == CompactOverlayActivity.Meter ? Visibility.Visible : Visibility.Collapsed;
        TimerText.Visibility = AudioMeter.Visibility;
        ProcessingIndicator.Visibility = activity == CompactOverlayActivity.Progress
            ? Visibility.Visible
            : Visibility.Collapsed;
        CancelHint.Visibility = activity.HasValue ? Visibility.Visible : Visibility.Collapsed;
        AudioMeter.Reset();
        ProcessingCue.Background = palette.Wave.Count > 2 ? WaveBrush() : Theme.Brush(accent);
    }

    private LinearGradientBrush WaveBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        var stops = palette.Wave.Count - 1;
        for (var index = 0; index < stops; index++)
        {
            brush.GradientStops.Add(new GradientStop(palette.Wave[index], (double)index / (stops - 1)));
        }

        brush.Freeze();
        return brush;
    }

    private void SetMark(Mark mark, Color accent)
    {
        var fill = Theme.Brush(accent);
        GlyphRecord.Visibility = mark == Mark.Record ? Visibility.Visible : Visibility.Collapsed;
        GlyphSpinner.Visibility = mark == Mark.Spinner ? Visibility.Visible : Visibility.Collapsed;
        GlyphCaret.Visibility = mark == Mark.Caret ? Visibility.Visible : Visibility.Collapsed;
        GlyphDisc.Visibility = mark is Mark.Check or Mark.Silence or Mark.Error ? Visibility.Visible : Visibility.Collapsed;
        GlyphNote.Visibility = mark == Mark.Note ? Visibility.Visible : Visibility.Collapsed;

        TallyLens.Fill = fill;
        TallyRing.Stroke = Theme.Brush(Theme.WithAlpha(accent, palette.HighContrast ? (byte)255 : (byte)120));
        SpinnerTrack.Stroke = fill;
        SpinnerArc.Stroke = fill;
        GlyphCaret.Stroke = fill;
        DiscFill.Fill = fill;
        NoteRing.Stroke = fill;
        NoteDot.Fill = fill;
        DiscMark.Data = mark switch
        {
            Mark.Check => Geometry.Parse("M6.1,10.4 L8.8,13.1 L13.9,7.3"),
            Mark.Silence => Geometry.Parse("M6.4,10 H13.6"),
            Mark.Error => Geometry.Parse("M10,5.6 V10.6 M10,13.9 V14.2"),
            _ => null,
        };
    }

    // The capsule is as wide as its content; while there is activity to show it keeps one width, so nothing jumps.
    private double MeasureWidth()
    {
        if (ActivityHost.Visibility == Visibility.Visible)
        {
            return ActivityWidth;
        }

        if (DetailText.Visibility == Visibility.Visible)
        {
            BalanceDetail();
        }

        Layout.InvalidateMeasure();
        Frame.InvalidateMeasure();
        Frame.Measure(new Size(MaximumWidth, double.PositiveInfinity));
        var width = Math.Ceiling(Frame.DesiredSize.Width);
        Stage.InvalidateMeasure();
        return width;
    }

    // Wrapped text is set to the narrowest width that keeps the same number of lines,
    // so a message never ends in a single stray word.
    private void BalanceDetail()
    {
        const double widest = 292;
        var margin = DetailText.Margin.Left + DetailText.Margin.Right;
        DetailText.Width = double.NaN;
        DetailText.MaxWidth = widest;
        DetailText.Measure(new Size(widest + margin, double.PositiveInfinity));
        var height = DetailText.DesiredSize.Height;
        double narrow = 96;
        var wide = widest;
        while (wide - narrow > 1)
        {
            var middle = (narrow + wide) / 2;
            DetailText.Measure(new Size(middle + margin, double.PositiveInfinity));
            if (DetailText.DesiredSize.Height > height + 0.5)
            {
                narrow = middle;
            }
            else
            {
                wide = middle;
            }
        }

        // An explicit width with a spare pixel: layout rounding must not shave the line and wrap it again.
        DetailText.Width = Math.Ceiling(wide) + 1;
    }

    private void Present(bool animateWidth)
    {
        hideVersion++;
        var from = Stage.ActualWidth;
        var target = MeasureWidth();
        Stage.BeginAnimation(WidthProperty, null);
        Stage.Width = target;
        if (!IsVisible) targetMonitor = OverlayMonitor.CaptureTarget();
        PositionAboveTaskbar();

        if (IsVisible)
        {
            // A fade-out may be running; bring the overlay back at once.
            ((Storyboard)Resources["FadeOutStoryboard"]).Stop(this);
            Stage.Opacity = 1;
            StageShift.Y = 0;
            if (animateWidth && UiEnvironment.Animations && from > 0 && Math.Abs(from - target) > 1)
            {
                var resize = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                };
                Stage.BeginAnimation(WidthProperty, resize);
            }

            return;
        }

        Stage.Opacity = 0;
        base.Show();
        // Moving the hidden HWND to a different-DPI monitor can update WPF's scale during Show.
        // Finish positioning before the transparent stage starts fading in.
        PositionAboveTaskbar();
        if (UiEnvironment.Animations)
        {
            ((Storyboard)Resources["FadeInStoryboard"]).Begin(this, true);
        }
        else
        {
            Stage.Opacity = 1;
            StageShift.Y = 0;
        }
    }

    // Listening: a ripple and a running clock. Recognising: a turning arc and a sliding cue. Typing: a blinking cursor.
    private void StartMotion(ActivationVisualState state)
    {
        if (state == ActivationVisualState.Listening)
        {
            listeningSince = DateTime.UtcNow;
            TimerText.Text = "0:00";
            timer.Start();
            AudioMeter.SetRecording(true);
        }

        var animate = UiEnvironment.Animations;
        switch (state)
        {
            case ActivationVisualState.Listening when animate:
                ((Storyboard)Resources["RippleStoryboard"]).Begin(this, true);
                break;
            case ActivationVisualState.Processing:
                if (animate)
                {
                    ((Storyboard)Resources["SpinStoryboard"]).Begin(this, true);
                }

                StartProgress(animate);
                break;
            case ActivationVisualState.Inserting when animate:
                ((Storyboard)Resources["BlinkStoryboard"]).Begin(this, true);
                break;
        }
    }

    private void StartProgress(bool animate)
    {
        UpdateLayout();
        var track = ProcessingIndicator.ActualWidth;
        if (track <= 0)
        {
            return;
        }

        var cue = Math.Round(track * 0.34);
        ProcessingCue.Width = cue;
        if (!animate)
        {
            // Without motion the cue rests in the middle: still an indeterminate bar, not a progress value.
            ProcessingCueShift.X = Math.Round((track - cue) / 2);
            return;
        }

        var slide = new DoubleAnimation(-cue, track, TimeSpan.FromSeconds(1.15))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            AccelerationRatio = 0.25,
            DecelerationRatio = 0.25,
        };
        ProcessingCueShift.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void StopPresentationMotion()
    {
        ((Storyboard)Resources["FadeInStoryboard"]).Remove(this);
        ((Storyboard)Resources["FadeOutStoryboard"]).Remove(this);
        Stage.BeginAnimation(WidthProperty, null);
    }

    private void StopMotion()
    {
        AudioMeter.SetRecording(false);
        timer.Stop();
        ((Storyboard)Resources["RippleStoryboard"]).Stop(this);
        ((Storyboard)Resources["SpinStoryboard"]).Stop(this);
        ((Storyboard)Resources["BlinkStoryboard"]).Stop(this);
        ProcessingCueShift.BeginAnimation(TranslateTransform.XProperty, null);
        Ripple.Opacity = 0;
        GlyphCaret.Opacity = 1;
    }

    /// <summary>For the UI preview tool: the recording clock as it looks some seconds into a dictation.</summary>
    internal void ShowElapsed(TimeSpan elapsed)
    {
        listeningSince = DateTime.UtcNow - elapsed;
        UpdateTimerText();
    }

    private void UpdateTimerText()
    {
        var elapsed = DateTime.UtcNow - listeningSince;
        var text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        if (TimerText.Text != text) TimerText.Text = text;
    }

    private void PositionAboveTaskbar()
    {
        if (positioning || closed) return;
        positioning = true;
        try
        {
            if (targetMonitor == 0) targetMonitor = OverlayMonitor.CaptureTarget();
            var handle = new WindowInteropHelper(this).EnsureHandle();
            // Window is Collapsed before its first Show, so measuring Window itself still yields zero.
            // Measure its content, including the unchanged 28-DIP shadow margin, at the final width.
            Stage.Measure(new Size(Width, double.PositiveInfinity));
            var desiredHeight = Stage.DesiredSize.Height;
            Stage.Arrange(new Rect(0, 0, Width, desiredHeight));
            var dpi = VisualTreeHelper.GetDpi(this);
            var workArea = OverlayMonitor.WorkArea(targetMonitor);
            // Position in physical screen coordinates. Scaling an absolute monitor origin as a DIP
            // would be wrong on mixed-DPI desktops; only scale the overlay's local dimensions.
            var placement = OverlayPositioning.BottomCenter(
                workArea, 0, 0, Width * dpi.DpiScaleX, desiredHeight * dpi.DpiScaleY, margin: 0);
            OverlayMonitor.Position(handle, placement.Left, placement.Top);
        }
        finally
        {
            positioning = false;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        UiEnvironment.ApplyTextFormatting(this);
        var handle = new WindowInteropHelper(this).Handle;
        var styles = NativeMethods.GetWindowLong(handle, ExtendedStyleIndex);
        Marshal.SetLastPInvokeError(0);
        // Never takes focus, stays out of Alt+Tab and lets the mouse through to the window below.
        var previousStyles = NativeMethods.SetWindowLong(
            handle,
            ExtendedStyleIndex,
            styles | NoActivateStyle | ToolWindowStyle | ClickThroughStyle);
        if (previousStyles == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not apply the no-activate overlay style.");
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(nint windowHandle, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(nint windowHandle, int index, int newLong);
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using VoiceInput.Core.Activation;
using VoiceInput.Windows.Appearance;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace VoiceInput.App;

public partial class MainWindow : Window, IActivationOverlay
{
    private const int ExtendedStyleIndex = -20;
    private const int NoActivateStyle = 0x08000000;
    private const int ToolWindowStyle = 0x00000080;
    private const double CompactMinimumHeight = 76;
    private const double CornerRadius = 16;
    private const double ScreenMargin = 28;

    private static readonly BrushConverter BrushConverter = new();
    private static readonly Color MeterIdle = Color.FromRgb(68, 72, 84);

    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTime listeningSince;
    private int hideVersion;
    private bool shutdownRequested;
    private bool closed;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) =>
        {
            closed = true;
            timer.Stop();
        };
        timer.Tick += (_, _) => UpdateTimerText();
    }

    public OverlayBackdropMode BackdropMode { get; private set; } = OverlayBackdropMode.TintOnly;

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
        DetailText.Visibility = Visibility.Collapsed;
        if (state is ActivationVisualState.Listening or ActivationVisualState.Processing)
        {
            var presentation = CompactOverlayPresentation.For(state);
            StatusText.Text = presentation.Title;
            SetActivity(presentation.Activity);
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
            SetActivity(null);
        }

        ApplyStateAppearance(state);
        UpdateMotion(state);
        ShowWithoutActivation();
    }

    public void ShowError(string message)
    {
        SetActivity(null);
        StatusText.Text = "Не удалось";
        DetailText.Text = message;
        DetailText.Visibility = Visibility.Visible;
        ApplyAccent("#FF6B83", "#80FF6B83");
        StopMotion();
        ShowWithoutActivation();
    }

    public void ShowStatus(string title, string detail)
    {
        SetActivity(null);
        StatusText.Text = title;
        DetailText.Text = detail;
        DetailText.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
        ApplyAccent("#A99BFF", "#78A99BFF");
        StopMotion();
        ShowWithoutActivation();
    }

    public void SetRecordingLevel(float level)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                _ = Dispatcher.BeginInvoke(() => SetRecordingLevel(level));
            }

            return;
        }

        if (AudioMeter.Visibility == Visibility.Visible)
        {
            AudioMeter.SetLevel(level);
        }
    }

    void IActivationOverlay.Hide()
    {
        if (!IsVisible || closed)
        {
            return;
        }

        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
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

    private static SolidColorBrush Brush(string value)
    {
        var brush = (SolidColorBrush)BrushConverter.ConvertFromString(value)!;
        brush.Freeze();
        return brush;
    }

    private void ShowWithoutActivation()
    {
        hideVersion++;
        PositionAboveTaskbar();
        if (IsVisible)
        {
            // A fade-out may be running; bring the card back at once.
            ((Storyboard)Resources["FadeOutStoryboard"]).Stop(this);
            Frame.Opacity = 1;
            FrameShift.Y = 0;
            return;
        }

        Frame.Opacity = 0;
        base.Show();
        if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
        {
            ((Storyboard)Resources["FadeInStoryboard"]).Begin(this, true);
        }
        else
        {
            Frame.Opacity = 1;
            FrameShift.Y = 0;
        }
    }

    private void SetActivity(CompactOverlayActivity? activity)
    {
        MinHeight = CompactMinimumHeight;
        ActivityHost.Visibility = activity.HasValue ? Visibility.Visible : Visibility.Collapsed;
        AudioMeter.Visibility = activity == CompactOverlayActivity.Meter ? Visibility.Visible : Visibility.Collapsed;
        ProcessingIndicator.Visibility = activity == CompactOverlayActivity.Progress
            ? Visibility.Visible
            : Visibility.Collapsed;
        CancelHint.Visibility = activity.HasValue ? Visibility.Visible : Visibility.Collapsed;
        AudioMeter.Reset();
    }

    // Listening: a ripple and a running clock. Recognising and typing: the light breathes. Everything else is still.
    private void UpdateMotion(ActivationVisualState state)
    {
        StopMotion();
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
        {
            return;
        }

        switch (state)
        {
            case ActivationVisualState.Listening:
                listeningSince = DateTime.UtcNow;
                TimerText.Text = "0:00";
                TimerText.Visibility = Visibility.Visible;
                timer.Start();
                ((Storyboard)Resources["RippleStoryboard"]).Begin(this, true);
                break;
            case ActivationVisualState.Processing:
            case ActivationVisualState.Inserting:
                ((Storyboard)Resources["BreathStoryboard"]).Begin(this, true);
                break;
        }
    }

    private void StopMotion()
    {
        timer.Stop();
        TimerText.Visibility = Visibility.Collapsed;
        ((Storyboard)Resources["RippleStoryboard"]).Stop(this);
        ((Storyboard)Resources["BreathStoryboard"]).Stop(this);
        Ripple.Opacity = 0;
        TallyLens.Opacity = 1;
    }

    private void UpdateTimerText()
    {
        var elapsed = DateTime.UtcNow - listeningSince;
        TimerText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
    }

    // One colour per stage: red while recording, violet while recognising, amber when nothing was heard,
    // blue while typing and green on success.
    private void ApplyStateAppearance(ActivationVisualState state)
    {
        var (accent, ring) = state switch
        {
            ActivationVisualState.Listening => ("#FF756D", "#80FF756D"),
            ActivationVisualState.Processing => ("#A99BFF", "#78A99BFF"),
            ActivationVisualState.NoSpeech => ("#F2C66D", "#78F2C66D"),
            ActivationVisualState.Inserting => ("#87B7FF", "#7887B7FF"),
            ActivationVisualState.Success => ("#69D6A4", "#7869D6A4"),
            _ => ("#A99BFF", "#78A99BFF"),
        };
        ApplyAccent(accent, ring);
    }

    private void ApplyAccent(string accent, string ring)
    {
        if (SystemParameters.HighContrast)
        {
            TallyLens.Fill = SystemColors.HighlightBrush;
            TallyRing.BorderBrush = SystemColors.HighlightBrush;
            Ripple.Stroke = SystemColors.HighlightBrush;
            ProcessingCue.Fill = SystemColors.HighlightBrush;
            StatusText.Foreground = SystemColors.WindowTextBrush;
            return;
        }

        var fill = Brush(accent);
        TallyLens.Fill = fill;
        TallyRing.BorderBrush = Brush(ring);
        Ripple.Stroke = fill;
        ProcessingCue.Fill = fill;
        StatusText.Foreground = Brush("#FAFAFC");
        var colour = fill.Color;
        AudioMeter.SetPalette(colour, MeterIdle);
        var glow = new DropShadowEffect { Color = colour, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.55 };
        glow.Freeze();
        AudioMeter.Effect = glow;
    }

    private void PositionAboveTaskbar()
    {
        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        var requestedWidth = double.IsNaN(Width) ? MinWidth : Width;
        var placement = OverlayPositioning.BottomCenter(
            new OverlayWorkArea(workArea.Left, workArea.Top, workArea.Width, workArea.Height),
            ActualWidth,
            ActualHeight,
            requestedWidth,
            MinHeight,
            margin: ScreenMargin);
        Left = placement.Left;
        Top = placement.Top;
    }

    private void ApplyNoActivateStyle()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var styles = NativeMethods.GetWindowLong(handle, ExtendedStyleIndex);
        Marshal.SetLastPInvokeError(0);
        var previousStyles = NativeMethods.SetWindowLong(
            handle,
            ExtendedStyleIndex,
            styles | NoActivateStyle | ToolWindowStyle);
        if (previousStyles == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not apply the no-activate overlay style.");
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyNoActivateStyle();
        var handle = new WindowInteropHelper(this).Handle;
        BackdropMode = AcrylicBackdrop.Apply(handle);
        if (BackdropMode == OverlayBackdropMode.Acrylic && !RoundedWindowRegion.TryApply(handle, CornerRadius))
        {
            AcrylicBackdrop.Disable(handle);
            BackdropMode = OverlayBackdropMode.TintOnly;
        }

        ApplySurface();
        if (BackdropMode == OverlayBackdropMode.Acrylic)
        {
            SizeChanged += OnWindowSizeChanged;
        }
    }

    private void ApplySurface()
    {
        if (SystemParameters.HighContrast)
        {
            Frame.Background = SystemColors.WindowBrush;
            Frame.BorderBrush = SystemColors.WindowTextBrush;
            Frame.Effect = null;
            DetailText.Foreground = SystemColors.WindowTextBrush;
            TimerText.Foreground = SystemColors.WindowTextBrush;
            CancelHint.Background = SystemColors.WindowBrush;
            CancelHint.BorderBrush = SystemColors.WindowTextBrush;
            CancelKeyText.Foreground = SystemColors.WindowTextBrush;
            ProcessingTrack.Background = SystemColors.GrayTextBrush;
            AudioMeter.SetPalette(SystemColors.HighlightColor, SystemColors.GrayTextColor);
            return;
        }

        Frame.Background = BackdropMode == OverlayBackdropMode.Acrylic ? Brush("#24101218") : Brush("#F20E1016");
        Frame.BorderBrush = Brush("#38FFFFFF");
        DetailText.Foreground = Brush("#B7BBC7");
        TimerText.Foreground = Brush("#8F94A8");
        CancelHint.Background = Brush("#141E2230");
        CancelHint.BorderBrush = Brush("#4AFFFFFF");
        CancelKeyText.Foreground = Brush("#C9CCD6");
        ProcessingTrack.Background = Brush("#3F565B6B");
        AudioMeter.SetPalette(Color.FromRgb(255, 117, 109), MeterIdle);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (BackdropMode == OverlayBackdropMode.Acrylic && !RoundedWindowRegion.TryApply(handle, CornerRadius))
        {
            AcrylicBackdrop.Disable(handle);
            BackdropMode = OverlayBackdropMode.TintOnly;
            ApplySurface();
            SizeChanged -= OnWindowSizeChanged;
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(nint windowHandle, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(nint windowHandle, int index, int newLong);
    }
}

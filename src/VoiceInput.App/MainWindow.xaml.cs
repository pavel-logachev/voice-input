using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using VoiceInput.Core.Activation;

namespace VoiceInput.App;

public partial class MainWindow : Window, IActivationOverlay
{
    private const int ExtendedStyleIndex = -20;
    private const int NoActivateStyle = 0x08000000;
    private const int ToolWindowStyle = 0x00000080;
    private const double CompactMinimumHeight = 64;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyNoActivateStyle();
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
                ActivationVisualState.NoSpeech => "Речь не распознана",
                ActivationVisualState.Inserting => "Вставляю",
                ActivationVisualState.Success => "Готово",
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
            };
            SetActivity(null);
        }

        StatusText.Foreground = Brush("#F8FAFC");

        PositionAboveTaskbar();
        if (!IsVisible)
        {
            base.Show();
        }
    }

    public void ShowError(string message)
    {
        SetActivity(null);
        StatusText.Text = "Ошибка";
        DetailText.Text = message;
        DetailText.Visibility = Visibility.Visible;
        StatusText.Foreground = Brush("#FB7185");
        PositionAboveTaskbar();
        if (!IsVisible)
        {
            base.Show();
        }
    }

    public void ShowStatus(string title, string detail)
    {
        SetActivity(null);
        StatusText.Text = title;
        DetailText.Text = detail;
        DetailText.Visibility = string.IsNullOrWhiteSpace(detail)
            ? Visibility.Collapsed
            : Visibility.Visible;
        StatusText.Foreground = Brush("#F8FAFC");
        PositionAboveTaskbar();
        if (!IsVisible)
        {
            base.Show();
        }
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

        if (AudioMeter.Visibility != Visibility.Visible)
        {
            return;
        }

        AudioMeter.SetLevel(level);
    }

    private void SetActivity(CompactOverlayActivity? activity)
    {
        MinHeight = CompactMinimumHeight;
        ActivityHost.Visibility = activity.HasValue ? Visibility.Visible : Visibility.Collapsed;
        AudioMeter.Visibility = activity == CompactOverlayActivity.Meter
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProcessingIndicator.Visibility = activity == CompactOverlayActivity.Progress
            ? Visibility.Visible
            : Visibility.Collapsed;
        AudioMeter.Reset();
    }

    private static SolidColorBrush Brush(string value) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;

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
            margin: 24);
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

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(nint windowHandle, int index);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(nint windowHandle, int index, int newLong);
    }
}

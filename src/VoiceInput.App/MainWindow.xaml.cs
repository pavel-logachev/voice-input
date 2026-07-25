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

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyNoActivateStyle();
    }

    public void Show(ActivationVisualState state)
    {
        (StatusText.Text, DetailText.Text, StatusDot.Fill) = state switch
        {
            ActivationVisualState.Listening =>
                ("Слушаю", "Говорите и отпустите Ctrl + Shift + Space", Brush("#22D3EE")),
            ActivationVisualState.Processing =>
                ("Распознаю локально", "GigaAM обрабатывает запись на этом компьютере", Brush("#A78BFA")),
            ActivationVisualState.NoSpeech =>
                ("Речь не распознана", "Попробуйте говорить ближе к микрофону", Brush("#F59E0B")),
            ActivationVisualState.Inserting =>
                ("Вставляю текст", "Фокус остаётся в исходном окне", Brush("#F59E0B")),
            ActivationVisualState.Success =>
                ("Готово", "Локальная расшифровка вставлена", Brush("#34D399")),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

        PositionAboveTaskbar();
        if (!IsVisible)
        {
            base.Show();
        }
    }

    public void ShowError(string message)
    {
        StatusText.Text = "Ошибка диктовки";
        DetailText.Text = message;
        StatusDot.Fill = Brush("#FB7185");
        PositionAboveTaskbar();
        if (!IsVisible)
        {
            base.Show();
        }
    }

    public void ShowStatus(string title, string detail)
    {
        StatusText.Text = title;
        DetailText.Text = detail;
        StatusDot.Fill = Brush("#22D3EE");
        PositionAboveTaskbar();
        if (!IsVisible)
        {
            base.Show();
        }
    }

    private static SolidColorBrush Brush(string value) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;

    private void PositionAboveTaskbar()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + ((workArea.Width - Width) / 2);
        Top = workArea.Bottom - Height - 24;
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

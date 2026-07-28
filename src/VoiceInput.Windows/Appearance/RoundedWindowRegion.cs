using System.Runtime.InteropServices;

namespace VoiceInput.Windows.Appearance;

public readonly record struct RoundedWindowRegionGeometry(
    int Right,
    int Bottom,
    int EllipseWidth,
    int EllipseHeight);

public static class RoundedWindowRegion
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerPreferenceRound = 2;

    public static RoundedWindowRegionGeometry CalculateGeometry(
        int clientWidth,
        int clientHeight,
        uint dpi,
        double logicalCornerRadius)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clientWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clientHeight);
        ArgumentOutOfRangeException.ThrowIfZero(dpi);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(logicalCornerRadius);

        var diameter = checked((int)Math.Round(
            logicalCornerRadius * 2 * dpi / 96,
            MidpointRounding.AwayFromZero));
        return new RoundedWindowRegionGeometry(
            clientWidth + 1,
            clientHeight + 1,
            diameter,
            diameter);
    }

    public static bool TryApply(nint windowHandle, double logicalCornerRadius)
    {
        if (windowHandle == nint.Zero ||
            !NativeMethods.GetClientRect(windowHandle, out var clientRect))
        {
            return false;
        }

        var dpi = NativeMethods.GetDpiForWindow(windowHandle);
        if (dpi == 0)
        {
            return false;
        }

        var geometry = CalculateGeometry(
            clientRect.Right - clientRect.Left,
            clientRect.Bottom - clientRect.Top,
            dpi,
            logicalCornerRadius);
        var region = NativeMethods.CreateRoundRectRgn(
            0,
            0,
            geometry.Right,
            geometry.Bottom,
            geometry.EllipseWidth,
            geometry.EllipseHeight);
        if (region == nint.Zero)
        {
            return false;
        }

        if (NativeMethods.SetWindowRgn(windowHandle, region, redraw: true) != 0)
        {
            var cornerPreference = DwmCornerPreferenceRound;
            return NativeMethods.DwmSetWindowAttribute(
                windowHandle,
                DwmWindowCornerPreference,
                ref cornerPreference,
                sizeof(int)) == 0;
        }

        _ = NativeMethods.DeleteObject(region);
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(nint windowHandle, out Rect clientRect);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(nint windowHandle);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern nint CreateRoundRectRgn(
            int left,
            int top,
            int right,
            int bottom,
            int ellipseWidth,
            int ellipseHeight);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowRgn(
            nint windowHandle,
            nint region,
            [MarshalAs(UnmanagedType.Bool)] bool redraw);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(nint graphicsObject);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(
            nint windowHandle,
            int attribute,
            ref int value,
            int valueSize);
    }
}

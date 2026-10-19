using System.Runtime.InteropServices;
using VoiceInput.Core.Activation;

namespace VoiceInput.App;

/// <summary>Monitor rectangles are physical pixels; conversion uses the overlay's current WPF DPI.</summary>
internal static class OverlayMonitor
{
    internal static nint CaptureTarget() => MonitorFromWindow(GetForegroundWindow(), 1 /* primary fallback */);

    internal static OverlayWorkArea WorkArea(nint monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            monitor = MonitorFromWindow(0, 1);
            info.Size = Marshal.SizeOf<MonitorInfo>();
            if (!GetMonitorInfo(monitor, ref info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        }
        return new OverlayWorkArea(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }

    internal static void Position(nint handle, double left, double top) =>
        SetWindowPos(handle, 0, (int)Math.Round(left), (int)Math.Round(top), 0, 0, 0x0015 /* no size, z-order or activation */);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}

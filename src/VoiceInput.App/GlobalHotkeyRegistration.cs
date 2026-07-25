using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace VoiceInput.App;

internal sealed class GlobalHotkeyRegistration : IDisposable
{
    private const int HotkeyId = 0x5649;
    private const int HotkeyMessage = 0x0312;
    private const uint ModifierShift = 0x0004;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierNoRepeat = 0x4000;
    private const uint VirtualKeySpace = 0x20;
    private static readonly nint MessageOnlyWindow = new(-3);

    private readonly HwndSource source;
    private bool disposed;

    public GlobalHotkeyRegistration()
    {
        var parameters = new HwndSourceParameters("VoiceInput.GlobalHotkey")
        {
            ParentWindow = MessageOnlyWindow,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };

        source = new HwndSource(parameters);
        source.AddHook(WindowProcedure);

        if (!NativeMethods.RegisterHotKey(
                source.Handle,
                HotkeyId,
                ModifierControl | ModifierShift | ModifierNoRepeat,
                VirtualKeySpace))
        {
            var error = Marshal.GetLastWin32Error();
            source.RemoveHook(WindowProcedure);
            source.Dispose();
            throw new Win32Exception(error, "Could not register Ctrl+Shift+Space as a global hotkey.");
        }
    }

    public event EventHandler? Activated;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        NativeMethods.UnregisterHotKey(source.Handle, HotkeyId);
        source.RemoveHook(WindowProcedure);
        source.Dispose();
    }

    private nint WindowProcedure(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == HotkeyMessage && wordParameter == HotkeyId)
        {
            handled = true;
            Activated?.Invoke(this, EventArgs.Empty);
        }

        return nint.Zero;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(
            nint windowHandle,
            int id,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(nint windowHandle, int id);
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace VoiceInput.App;

internal sealed class GlobalHotkeyRegistration : IDisposable
{
    private const int HotkeyId = 0x5649;
    private const int HotkeyMessage = 0x0312;
    private const int LowLevelKeyboardHook = 13;
    private const int KeyDownMessage = 0x0100;
    private const int SystemKeyDownMessage = 0x0104;
    private const uint ModifierShift = 0x0004;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierNoRepeat = 0x4000;
    private const uint VirtualKeySpace = 0x20;
    private const uint VirtualKeyEscape = 0x1B;
    private static readonly nint MessageOnlyWindow = new(-3);

    private readonly HwndSource source;
    private readonly LowLevelKeyboardProcedure cancellationHookProcedure;
    private nint cancellationHook;
    private bool disposed;

    public GlobalHotkeyRegistration()
    {
        cancellationHookProcedure = CancellationHookProcedure;
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

    public event EventHandler? CancellationRequested;

    public void EnableCancellation()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (cancellationHook != nint.Zero)
        {
            return;
        }

        cancellationHook = NativeMethods.SetWindowsHookEx(
            LowLevelKeyboardHook,
            cancellationHookProcedure,
            nint.Zero,
            0);
        if (cancellationHook == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not monitor Escape for dictation cancellation.");
        }
    }

    public void DisableCancellation()
    {
        if (cancellationHook == nint.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(cancellationHook);
        cancellationHook = nint.Zero;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DisableCancellation();
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

    private nint CancellationHookProcedure(int code, nint wordParameter, nint longParameter)
    {
        if (code >= 0 &&
            (wordParameter == KeyDownMessage || wordParameter == SystemKeyDownMessage) &&
            Marshal.ReadInt32(longParameter) == VirtualKeyEscape)
        {
            CancellationRequested?.Invoke(this, EventArgs.Empty);
            return 1;
        }

        return NativeMethods.CallNextHookEx(cancellationHook, code, wordParameter, longParameter);
    }

    private delegate nint LowLevelKeyboardProcedure(int code, nint wordParameter, nint longParameter);

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

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint SetWindowsHookEx(
            int hookId,
            LowLevelKeyboardProcedure procedure,
            nint moduleHandle,
            uint threadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(nint hookHandle);

        [DllImport("user32.dll")]
        public static extern nint CallNextHookEx(
            nint hookHandle,
            int code,
            nint wordParameter,
            nint longParameter);
    }
}

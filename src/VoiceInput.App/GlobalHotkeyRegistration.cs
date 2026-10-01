using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using VoiceInput.Windows.Hotkeys;

namespace VoiceInput.App;

internal sealed class GlobalHotkeyRegistration : IDisposable
{
    private const int HotkeyMessage = 0x0312;
    private const int LowLevelKeyboardHook = 13;
    private const int KeyDownMessage = 0x0100;
    private const int SystemKeyDownMessage = 0x0104;
    private const uint ModifierShift = 0x0004;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierNoRepeat = 0x4000;
    private const uint VirtualKeySpace = 0x20;
    private const uint VirtualKeyK = 0x4B;
    private const uint VirtualKeyEscape = 0x1B;
    private static readonly nint MessageOnlyWindow = new(-3);

    private readonly HwndSource source;
    private readonly LowLevelKeyboardProcedure cancellationHookProcedure;
    private readonly EscapeCancellationPolicy cancellationPolicy;
    private readonly HotkeyAvailability availability;
    private nint cancellationHook;
    private bool disposed;

    public GlobalHotkeyRegistration(Func<bool> canCancel)
    {
        cancellationPolicy = new EscapeCancellationPolicy(canCancel);
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

        // A hotkey taken by another program is reported, not fatal: the other one and the tray keep working.
        availability = HotkeyAvailability.RegisterAll(
            id => id switch
            {
                HotkeyAvailability.HoldHotkeyId => NativeMethods.RegisterHotKey(
                    source.Handle, id, ModifierControl | ModifierShift | ModifierNoRepeat, VirtualKeySpace),
                HotkeyAvailability.ToggleHotkeyId => NativeMethods.RegisterHotKey(
                    source.Handle, id, ModifierControl | ModifierShift | ModifierNoRepeat, VirtualKeyK),
                _ => throw new ArgumentOutOfRangeException(nameof(id)),
            },
            Marshal.GetLastWin32Error);
    }

    public event EventHandler? Activated;

    public event EventHandler? ToggleActivated;

    public event EventHandler? CancellationRequested;

    public bool HoldHotkeyAvailable => availability.HoldHotkeyAvailable;

    public bool ToggleHotkeyAvailable => availability.ToggleHotkeyAvailable;

    public int HoldHotkeyError => availability.HoldHotkeyError;

    public int ToggleHotkeyError => availability.ToggleHotkeyError;

    public string? GetConflictMessage() => availability.CreateConflictMessage();

    public void EnableCancellation()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationPolicy.BeginSession();
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
        cancellationPolicy.EndSession();
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
        if (availability.ToggleHotkeyAvailable)
        {
            NativeMethods.UnregisterHotKey(source.Handle, HotkeyAvailability.ToggleHotkeyId);
        }

        if (availability.HoldHotkeyAvailable)
        {
            NativeMethods.UnregisterHotKey(source.Handle, HotkeyAvailability.HoldHotkeyId);
        }

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
        if (message == HotkeyMessage && wordParameter == HotkeyAvailability.HoldHotkeyId)
        {
            handled = true;
            Activated?.Invoke(this, EventArgs.Empty);
        }
        else if (message == HotkeyMessage && wordParameter == HotkeyAvailability.ToggleHotkeyId)
        {
            handled = true;
            ToggleActivated?.Invoke(this, EventArgs.Empty);
        }

        return nint.Zero;
    }

    // Runs inside the low-level keyboard hook: answer at once, cancel later on the UI thread.
    private nint CancellationHookProcedure(int code, nint wordParameter, nint longParameter)
    {
        if (code >= 0 &&
            (wordParameter == KeyDownMessage || wordParameter == SystemKeyDownMessage) &&
            Marshal.ReadInt32(longParameter) == VirtualKeyEscape)
        {
            var decision = cancellationPolicy.Decide();
            if (decision.Kind != EscapeDecisionKind.PassThrough)
            {
                if (decision.Kind == EscapeDecisionKind.QueueCancellation)
                {
                    QueueCancellation(decision.Generation);
                }

                return 1;
            }
        }

        return NativeMethods.CallNextHookEx(cancellationHook, code, wordParameter, longParameter);
    }

    private void QueueCancellation(long generation)
    {
        var dispatcher = source.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            dispatcher
                .BeginInvoke(
                    DispatcherPriority.Send,
                    () =>
                    {
                        if (cancellationPolicy.TryConsumeQueuedCancellation(generation))
                        {
                            CancellationRequested?.Invoke(this, EventArgs.Empty);
                        }
                    })
                .Task.ContinueWith(
                    static completed => completed.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }
        catch (InvalidOperationException)
        {
            // The dispatcher shut down between the check and the call.
        }
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

using System.ComponentModel;
using System.Runtime.InteropServices;
using VoiceInput.Core.Activation;

namespace VoiceInput.Windows.Input;

public interface INativeTextControlApi
{
    nint GetForegroundWindow();

    nint GetFocusedControl(nint topLevelWindow);

    bool IsChild(nint parent, nint child);

    string GetClassName(nint windowHandle);

    void ReplaceSelection(nint control, string text);
}

public sealed class WindowsTextInserter : ITextInserter
{
    private readonly INativeTextControlApi nativeApi;
    private readonly ITextInserter fallback;

    public WindowsTextInserter()
        : this(new NativeTextControlApi(), new WindowsClipboardTextInserter())
    {
    }

    public WindowsTextInserter(INativeTextControlApi nativeApi, ITextInserter fallback)
    {
        this.nativeApi = nativeApi;
        this.fallback = fallback;
    }

    public async ValueTask InsertAsync(
        InputTarget target,
        string text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureForeground(target.WindowHandle);

        var focusedControl = nativeApi.GetFocusedControl(target.WindowHandle);
        if (focusedControl == nint.Zero ||
            (focusedControl != target.WindowHandle && !nativeApi.IsChild(target.WindowHandle, focusedControl)) ||
            !IsNativeTextControl(nativeApi.GetClassName(focusedControl)))
        {
            await fallback.InsertAsync(target, text, cancellationToken);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        EnsureForeground(target.WindowHandle);
        nativeApi.ReplaceSelection(focusedControl, text);
    }

    private static bool IsNativeTextControl(string className) =>
        className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
        className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
        className.Contains(".EDIT.", StringComparison.OrdinalIgnoreCase);

    private void EnsureForeground(nint expected)
    {
        var actual = nativeApi.GetForegroundWindow();
        if (actual != expected)
        {
            throw new TargetFocusChangedException(expected, actual);
        }
    }
}

internal sealed class NativeTextControlApi : INativeTextControlApi
{
    private const uint EmReplaceSelection = 0x00C2;
    private const uint SendMessageTimeoutBlock = 0x0001;
    private const uint SendMessageTimeoutAbortIfHung = 0x0002;

    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public nint GetFocusedControl(nint topLevelWindow)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(topLevelWindow, out _);
        if (threadId == 0)
        {
            return nint.Zero;
        }

        var info = new GuiThreadInfo
        {
            Size = (uint)Marshal.SizeOf<GuiThreadInfo>(),
        };
        return NativeMethods.GetGUIThreadInfo(threadId, ref info) ? info.FocusWindow : nint.Zero;
    }

    public bool IsChild(nint parent, nint child) => NativeMethods.IsChild(parent, child);

    public string GetClassName(nint windowHandle)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassName(windowHandle, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    public void ReplaceSelection(nint control, string text)
    {
        var succeeded = NativeMethods.SendMessageTimeout(
            control,
            EmReplaceSelection,
            1,
            text,
            SendMessageTimeoutBlock | SendMessageTimeoutAbortIfHung,
            1_000,
            out _);
        if (succeeded == nint.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(
                error == 0 ? 1460 : error,
                "The target text control did not accept the dictated text.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint ActiveWindow;
        public nint FocusWindow;
        public nint CaptureWindow;
        public nint MenuOwnerWindow;
        public nint MoveSizeWindow;
        public nint CaretWindow;
        public System.Drawing.Rectangle CaretRectangle;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo threadInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsChild(nint parent, nint child);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetClassName(nint windowHandle, [Out] char[] className, int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint SendMessageTimeout(
            nint windowHandle,
            uint message,
            nuint wordParameter,
            string longParameter,
            uint flags,
            uint timeoutMilliseconds,
            out nuint result);
    }
}

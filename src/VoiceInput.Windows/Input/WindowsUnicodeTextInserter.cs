using System.ComponentModel;
using System.Runtime.InteropServices;
using VoiceInput.Core.Activation;

namespace VoiceInput.Windows.Input;

public interface IWindowsInputApi
{
    nint GetForegroundWindow();

    void Send(IReadOnlyList<UnicodeKeyStroke> strokes);
}

public sealed class TargetFocusChangedException(nint expected, nint actual)
    : InvalidOperationException(
        $"The foreground window changed before insertion. Expected 0x{expected:X}, actual 0x{actual:X}.");

public sealed class WindowsUnicodeTextInserter : ITextInserter
{
    private readonly IWindowsInputApi api;

    public WindowsUnicodeTextInserter()
        : this(new NativeWindowsInputApi())
    {
    }

    public WindowsUnicodeTextInserter(IWindowsInputApi api)
    {
        this.api = api;
    }

    public ValueTask InsertAsync(
        InputTarget target,
        string text,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var foregroundWindow = api.GetForegroundWindow();
        if (foregroundWindow != target.WindowHandle)
        {
            throw new TargetFocusChangedException(target.WindowHandle, foregroundWindow);
        }

        api.Send(UnicodeInputBuilder.Build(text));
        return ValueTask.CompletedTask;
    }
}

internal sealed class NativeWindowsInputApi : IWindowsInputApi
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public void Send(IReadOnlyList<UnicodeKeyStroke> strokes)
    {
        if (strokes.Count == 0)
        {
            return;
        }

        var inputs = new Input[strokes.Count];
        for (var index = 0; index < strokes.Count; index++)
        {
            var stroke = strokes[index];
            inputs[index] = new Input
            {
                Type = InputKeyboard,
                Union = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        ScanCode = stroke.ScanCode,
                        Flags = KeyEventUnicode | (stroke.IsKeyUp ? KeyEventKeyUp : 0),
                    },
                },
            };
        }

        var sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<Input>());

        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput did not send the complete Unicode sequence.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(
            uint inputCount,
            [In] Input[] inputs,
            int inputSize);
    }
}

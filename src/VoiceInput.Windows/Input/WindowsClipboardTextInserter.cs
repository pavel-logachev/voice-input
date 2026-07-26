using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VoiceInput.Windows.Input;

public readonly record struct ClipboardSnapshot(object? State);

public interface IClipboardInsertionApi
{
    nint GetForegroundWindow();

    ClipboardSnapshot CaptureClipboard();

    void SetText(string text);

    bool IsTemporaryClipboardCurrent();

    void SendPaste();

    void RestoreClipboard(ClipboardSnapshot snapshot);
}

public interface IClipboardPasteDelay
{
    ValueTask DelayAsync();
}

public sealed class WindowsClipboardTextInserter : Core.Activation.ITextInserter
{
    private readonly IClipboardInsertionApi api;
    private readonly IClipboardPasteDelay pasteDelay;

    public WindowsClipboardTextInserter()
        : this(new NativeClipboardInsertionApi(), new SystemClipboardPasteDelay())
    {
    }

    public WindowsClipboardTextInserter(
        IClipboardInsertionApi api,
        IClipboardPasteDelay pasteDelay)
    {
        this.api = api;
        this.pasteDelay = pasteDelay;
    }

    public async ValueTask InsertAsync(
        Core.Activation.InputTarget target,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureForeground(target.WindowHandle);

        var snapshot = api.CaptureClipboard();
        var clipboardWritten = false;
        try
        {
            api.SetText(text);
            clipboardWritten = true;

            cancellationToken.ThrowIfCancellationRequested();
            EnsureForeground(target.WindowHandle);
            api.SendPaste();
            await pasteDelay.DelayAsync();
        }
        finally
        {
            if (clipboardWritten && api.IsTemporaryClipboardCurrent())
            {
                api.RestoreClipboard(snapshot);
            }
        }
    }

    private void EnsureForeground(nint expected)
    {
        var actual = api.GetForegroundWindow();
        if (actual != expected)
        {
            throw new TargetFocusChangedException(expected, actual);
        }
    }
}

internal sealed class SystemClipboardPasteDelay : IClipboardPasteDelay
{
    public async ValueTask DelayAsync() =>
        await Task.Delay(TimeSpan.FromMilliseconds(120));
}

internal sealed class NativeClipboardInsertionApi : IClipboardInsertionApi
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyV = 0x56;
    private const int ClipboardRetryCount = 8;
    private const string ClipboardMarkerFormat = "VoiceInput.ClipboardGuard";
    private static readonly uint ClipboardMarkerFormatId = NativeMethods.RegisterClipboardFormat(ClipboardMarkerFormat);

    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public ClipboardSnapshot CaptureClipboard()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var source = System.Windows.Clipboard.GetDataObject();
                if (source is null)
                {
                    return default;
                }

                var snapshot = new System.Windows.DataObject();
                foreach (var format in source.GetFormats(false).Distinct(StringComparer.Ordinal))
                {
                    var value = source.GetData(format, false);
                    if (value is not null)
                    {
                        snapshot.SetData(format, CloneClipboardValue(value));
                    }
                }

                return new ClipboardSnapshot(snapshot);
            }
            catch (ExternalException) when (attempt < ClipboardRetryCount - 1)
            {
                Thread.Sleep(20);
            }
        }
    }

    public void SetText(string text)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var dataObject = new System.Windows.DataObject();
                dataObject.SetData(System.Windows.DataFormats.UnicodeText, text);
                dataObject.SetData(ClipboardMarkerFormat, new byte[] { 1 }, false);
                System.Windows.Clipboard.SetDataObject(dataObject, false);
                return;
            }
            catch (ExternalException) when (attempt < ClipboardRetryCount - 1)
            {
                Thread.Sleep(20);
            }
        }
    }

    public bool IsTemporaryClipboardCurrent() =>
        ClipboardMarkerFormatId != 0 && NativeMethods.IsClipboardFormatAvailable(ClipboardMarkerFormatId);

    public void SendPaste()
    {
        var inputs = new[]
        {
            CreateKeyboardInput(VirtualKeyControl, false),
            CreateKeyboardInput(VirtualKeyV, false),
            CreateKeyboardInput(VirtualKeyV, true),
            CreateKeyboardInput(VirtualKeyControl, true),
        };

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "SendInput did not send the complete paste sequence.");
        }
    }

    public void RestoreClipboard(ClipboardSnapshot snapshot)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (snapshot.State is System.Windows.DataObject dataObject)
                {
                    System.Windows.Clipboard.SetDataObject(dataObject, true);
                }
                else
                {
                    System.Windows.Clipboard.Clear();
                }

                return;
            }
            catch (ExternalException) when (attempt < ClipboardRetryCount - 1)
            {
                Thread.Sleep(20);
            }
        }
    }

    private static object CloneClipboardValue(object value) => value switch
    {
        byte[] bytes => bytes.ToArray(),
        string[] paths => paths.ToArray(),
        MemoryStream stream => new MemoryStream(stream.ToArray(), writable: false),
        System.Windows.Media.Imaging.BitmapSource bitmap => bitmap.CloneCurrentValue(),
        _ => value,
    };

    private static Input CreateKeyboardInput(ushort virtualKey, bool isKeyUp) =>
        new()
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = isKeyUp ? KeyEventKeyUp : 0,
                },
            },
        };

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint RegisterClipboardFormat(string format);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(
            uint inputCount,
            [In] Input[] inputs,
            int inputSize);


    }
}

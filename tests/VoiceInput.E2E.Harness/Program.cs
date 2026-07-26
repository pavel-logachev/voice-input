using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace VoiceInput.E2E.Harness;

internal static class Program
{
    private const string DefaultExpectedText = "Диктовка работает.";

    [STAThread]
    private static void Main(string[] args)
    {
        var expectedText = args.Length > 0 ? args[0] : DefaultExpectedText;
        var cancelMode = string.Equals(
            Environment.GetEnvironmentVariable("VOICE_INPUT_E2E_CANCEL"),
            "1",
            StringComparison.Ordinal);
        var clipboardSentinel = Environment.GetEnvironmentVariable("VOICE_INPUT_E2E_CLIPBOARD_SENTINEL");
        Console.OutputEncoding = Encoding.UTF8;
        Forms.Application.EnableVisualStyles();
        Forms.Application.SetCompatibleTextRenderingDefault(false);
        var previousForegroundWindow = NativeMethods.GetForegroundWindow();

        using var form = new Forms.Form
        {
            Text = "Voice Input E2E Harness",
            Width = 560,
            Height = 180,
            StartPosition = Forms.FormStartPosition.CenterScreen,
        };
        using var textBox = new Forms.TextBox
        {
            Dock = Forms.DockStyle.Fill,
            Font = new System.Drawing.Font("Segoe UI", 18),
            Multiline = true,
            AccessibleName = "Voice Input E2E target",
        };
        form.Controls.Add(textBox);

        form.Shown += async (_, _) =>
        {
            await Task.Delay(250);
            BringToForeground(form.Handle);
            textBox.Focus();
            await Task.Delay(100);

            var foregroundWindow = NativeMethods.GetForegroundWindow();
            Console.WriteLine(
                $"E2E_READY pid={Environment.ProcessId} hwnd=0x{form.Handle:X} " +
                $"foreground=0x{foregroundWindow:X}");
            if (foregroundWindow != form.Handle)
            {
                Console.Error.WriteLine(
                    $"E2E_PRECONDITION_FAIL expected_foreground=0x{form.Handle:X} " +
                    $"actual_foreground=0x{foregroundWindow:X}");
                Environment.ExitCode = 2;
                form.Close();
                return;
            }

            if (cancelMode)
            {
                SendActivationHotkeyDown();
                await Task.Delay(200);
                SendEscape();
                SendActivationHotkeyUp();
                await Task.Delay(1_000);

                if (textBox.Text.Length == 0)
                {
                    Console.WriteLine("E2E_CANCEL_PASS text=");
                    Environment.ExitCode = 0;
                }
                else
                {
                    Console.Error.WriteLine($"E2E_CANCEL_FAIL actual={textBox.Text}");
                    Environment.ExitCode = 1;
                }

                form.Close();
                return;
            }

            if (!string.IsNullOrEmpty(clipboardSentinel))
            {
                Forms.Clipboard.SetText(clipboardSentinel);
            }

            SendActivationHotkey();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && textBox.Text != expectedText)
            {
                await Task.Delay(50);
            }

            if (textBox.Text == expectedText)
            {
                await Task.Delay(250);
                if (!string.IsNullOrEmpty(clipboardSentinel) &&
                    Forms.Clipboard.GetText() != clipboardSentinel)
                {
                    Console.Error.WriteLine($"E2E_CLIPBOARD_FAIL actual={Forms.Clipboard.GetText()}");
                    Environment.ExitCode = 1;
                    form.Close();
                    return;
                }

                Console.WriteLine($"E2E_PASS text={textBox.Text}");
                Environment.ExitCode = 0;
            }
            else
            {
                Console.Error.WriteLine($"E2E_FAIL expected={expectedText} actual={textBox.Text}");
                Environment.ExitCode = 1;
            }

            form.Close();
        };

        form.FormClosed += (_, _) =>
        {
            if (previousForegroundWindow != nint.Zero)
            {
                NativeMethods.SetForegroundWindow(previousForegroundWindow);
            }
        };

        Forms.Application.Run(form);
    }

    private static void BringToForeground(nint windowHandle)
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var currentThread = NativeMethods.GetCurrentThreadId();
        var foregroundThread = foregroundWindow == nint.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, attach: true);

        try
        {
            NativeMethods.BringWindowToTop(windowHandle);
            NativeMethods.SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, attach: false);
            }
        }
    }

    private static void SendActivationHotkey()
    {
        var inputs = new[]
        {
            Keyboard(0x11, 0),
            Keyboard(0x10, 0),
            Keyboard(0x20, 0),
            Keyboard(0x20, 0x0002),
            Keyboard(0x10, 0x0002),
            Keyboard(0x11, 0x0002),
        };

        Send(inputs, "activation hotkey");
    }

    private static void SendActivationHotkeyDown() => Send(
        [Keyboard(0x11, 0), Keyboard(0x10, 0), Keyboard(0x20, 0)],
        "activation hotkey down");

    private static void SendActivationHotkeyUp() => Send(
        [Keyboard(0x20, 0x0002), Keyboard(0x10, 0x0002), Keyboard(0x11, 0x0002)],
        "activation hotkey up");

    private static void SendEscape() => Send(
        [Keyboard(0x1B, 0), Keyboard(0x1B, 0x0002)],
        "Escape");

    private static void Send(Input[] inputs, string description)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"E2E harness could not send {description}.");
        }
    }

    private static Input Keyboard(ushort virtualKey, uint flags) => new()
    {
        Type = 1,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = flags,
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
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
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

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(nint windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(nint windowHandle);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint inputCount, [In] Input[] inputs, int inputSize);
    }
}

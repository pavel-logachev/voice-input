using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace VoiceInput.E2E.Harness;

internal static class Program
{
    private const string ExpectedText = "Диктовка работает.";

    [STAThread]
    private static void Main()
    {
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
            textBox.Focus();
            await Task.Delay(250);
            SendActivationHotkey();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && textBox.Text != ExpectedText)
            {
                await Task.Delay(50);
            }

            if (textBox.Text == ExpectedText)
            {
                Console.WriteLine($"E2E_PASS text={textBox.Text}");
                Environment.ExitCode = 0;
            }
            else
            {
                Console.Error.WriteLine($"E2E_FAIL expected={ExpectedText} actual={textBox.Text}");
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

    private static void SendActivationHotkey()
    {
        const ushort control = 0x11;
        const ushort shift = 0x10;
        const ushort space = 0x20;
        const uint keyUp = 0x0002;

        var inputs = new[]
        {
            Keyboard(control, 0),
            Keyboard(shift, 0),
            Keyboard(space, 0),
            Keyboard(space, keyUp),
            Keyboard(shift, keyUp),
            Keyboard(control, keyUp),
        };

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "E2E harness could not send the activation hotkey.");
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

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint inputCount, [In] Input[] inputs, int inputSize);
    }
}

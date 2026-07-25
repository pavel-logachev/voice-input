using System.Runtime.InteropServices;
using VoiceInput.Core.Activation;

namespace VoiceInput.Windows.Targeting;

public interface IForegroundWindowApi
{
    nint GetForegroundWindow();

    uint GetProcessId(nint windowHandle);
}

public sealed class ForegroundTargetCapture : IInputTargetCapture
{
    private readonly IForegroundWindowApi api;

    public ForegroundTargetCapture()
        : this(new NativeForegroundWindowApi())
    {
    }

    public ForegroundTargetCapture(IForegroundWindowApi api)
    {
        this.api = api;
    }

    public InputTarget Capture()
    {
        var windowHandle = api.GetForegroundWindow();
        return new InputTarget(windowHandle, api.GetProcessId(windowHandle));
    }
}

internal sealed class NativeForegroundWindowApi : IForegroundWindowApi
{
    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public uint GetProcessId(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return 0;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        return threadId == 0 ? 0 : processId;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
    }
}

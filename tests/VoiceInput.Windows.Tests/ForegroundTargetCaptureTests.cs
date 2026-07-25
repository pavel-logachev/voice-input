using VoiceInput.Windows.Targeting;

namespace VoiceInput.Windows.Tests.Targeting;

public sealed class ForegroundTargetCaptureTests
{
    [Fact]
    public void CapturePreservesWindowHandleAndProcessId()
    {
        var capture = new ForegroundTargetCapture(new FixedForegroundWindowApi((nint)42, 7));

        var target = capture.Capture();

        Assert.Equal((nint)42, target.WindowHandle);
        Assert.Equal((uint)7, target.ProcessId);
    }

    private sealed class FixedForegroundWindowApi(nint handle, uint processId) : IForegroundWindowApi
    {
        public nint GetForegroundWindow() => handle;

        public uint GetProcessId(nint windowHandle)
        {
            Assert.Equal(handle, windowHandle);
            return processId;
        }
    }
}

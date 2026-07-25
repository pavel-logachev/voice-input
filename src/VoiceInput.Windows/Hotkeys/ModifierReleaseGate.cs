using System.Runtime.InteropServices;
using VoiceInput.Core.Activation;

namespace VoiceInput.Windows.Hotkeys;

public interface IActivationKeyState
{
    bool IsAnyActivationKeyPressed();
}

public interface IHotkeyPollDelay
{
    ValueTask DelayAsync(CancellationToken cancellationToken);
}

public sealed class ModifierReleaseGate : IModifierReleaseGate
{
    private readonly IActivationKeyState keyState;
    private readonly IHotkeyPollDelay pollDelay;

    public ModifierReleaseGate()
        : this(new NativeActivationKeyState(), new SystemHotkeyPollDelay())
    {
    }

    public ModifierReleaseGate(IActivationKeyState keyState, IHotkeyPollDelay pollDelay)
    {
        this.keyState = keyState;
        this.pollDelay = pollDelay;
    }

    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        while (keyState.IsAnyActivationKeyPressed())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await pollDelay.DelayAsync(cancellationToken);
        }
    }
}

internal sealed class SystemHotkeyPollDelay : IHotkeyPollDelay
{
    public async ValueTask DelayAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
    }
}

internal sealed class NativeActivationKeyState : IActivationKeyState
{
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeySpace = 0x20;

    public bool IsAnyActivationKeyPressed() =>
        IsPressed(VirtualKeyControl) ||
        IsPressed(VirtualKeyShift) ||
        IsPressed(VirtualKeySpace);

    private static bool IsPressed(int virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int virtualKey);
    }
}

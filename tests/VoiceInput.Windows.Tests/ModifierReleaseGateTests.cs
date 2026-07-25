using VoiceInput.Windows.Hotkeys;

namespace VoiceInput.Windows.Tests.Hotkeys;

public sealed class ModifierReleaseGateTests
{
    [Fact]
    public async Task GateWaitsUntilEveryActivationKeyIsReleased()
    {
        var keyState = new SequenceActivationKeyState(true, true, false);
        var delay = new CountingPollDelay();
        var gate = new ModifierReleaseGate(keyState, delay);

        await gate.WaitAsync(CancellationToken.None);

        Assert.Equal(3, keyState.ReadCount);
        Assert.Equal(2, delay.CallCount);
    }

    private sealed class SequenceActivationKeyState(params bool[] states) : IActivationKeyState
    {
        private int index;

        public int ReadCount { get; private set; }

        public bool IsAnyActivationKeyPressed()
        {
            ReadCount++;
            return states[Math.Min(index++, states.Length - 1)];
        }
    }

    private sealed class CountingPollDelay : IHotkeyPollDelay
    {
        public int CallCount { get; private set; }

        public ValueTask DelayAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }
}

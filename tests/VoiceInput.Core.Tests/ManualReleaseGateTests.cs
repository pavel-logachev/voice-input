using VoiceInput.Core.Activation;

namespace VoiceInput.Core.Tests.Activation;

public sealed class ManualReleaseGateTests
{
    [Fact]
    public async Task WaitAsyncCompletesOnlyAfterRelease()
    {
        var gate = new ManualReleaseGate();

        var wait = gate.WaitAsync(CancellationToken.None).AsTask();

        Assert.False(wait.IsCompleted);
        Assert.True(gate.Release());
        await wait.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(gate.Release());
    }

    [Fact]
    public async Task WaitAsyncObservesCancellation()
    {
        var gate = new ManualReleaseGate();
        using var cancellation = new CancellationTokenSource();

        var wait = gate.WaitAsync(cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}

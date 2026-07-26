namespace VoiceInput.Core.Activation;

public sealed class ManualReleaseGate : IModifierReleaseGate
{
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Release() => completion.TrySetResult();

    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}

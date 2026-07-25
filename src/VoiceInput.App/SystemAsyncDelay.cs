using VoiceInput.Core.Activation;

namespace VoiceInput.App;

internal sealed class SystemAsyncDelay : IAsyncDelay
{
    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken);
    }
}

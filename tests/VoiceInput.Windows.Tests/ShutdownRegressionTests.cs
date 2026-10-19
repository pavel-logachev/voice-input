using System.Collections.Concurrent;
using VoiceInput.Core.Audio;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests;

public sealed class ShutdownRegressionTests
{
    [Fact]
    public async Task CancelledLocalTranscriptionCanDisposeWithoutPumpingCallingContext()
    {
        var context = new QueuedContext();
        var client = new WaitingClient();
        await using var engine = new LocalEngineTranscriber(() => (client, client));
        using var cancel = new CancellationTokenSource();
        var previous = SynchronizationContext.Current;
        Task transcription;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            transcription = engine.TranscribeAsync(new RecordedAudio(new float[1600], 16000), cancel.Token).AsTask();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        cancel.Cancel();
        var disposal = Task.Run(async () => await engine.DisposeAsync());
        var completed = await Task.WhenAny(disposal, Task.Delay(500));
        // Drain only after observing the outcome, so a failure never strands a test worker.
        context.Drain();
        await disposal.WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transcription);
        Assert.Same(disposal, completed);
    }

    [Fact]
    public async Task ShutdownHasDeadlineAndForcesUncooperativeResource()
    {
        var resource = new StuckResource();
        var killed = false;
        await VoiceInput.App.BoundedShutdown.DisposeAsync(resource, TimeSpan.FromMilliseconds(30), () => killed = true)
            .WaitAsync(TimeSpan.FromSeconds(1));
        resource.Finish.SetResult();
        Assert.True(killed);
    }
    private sealed class StuckResource : IAsyncDisposable
    {
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask DisposeAsync() => new(Finish.Task);
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> queue = new();
        public override void Post(SendOrPostCallback d, object? state) => queue.Enqueue((d, state));
        public void Drain() { while (queue.TryDequeue(out var item)) item.Callback(item.State); }
    }
    private sealed class WaitingClient : IAsrSegmentClient, IAsyncDisposable
    {
        public async ValueTask<string> TranscribeSegmentAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken cancellationToken)
        { await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); return ""; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

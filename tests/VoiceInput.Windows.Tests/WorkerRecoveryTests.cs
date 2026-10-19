using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests;

public sealed class WorkerRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrashOrHangInvalidatesAndNextDictationRestarts(bool hang)
    {
        var created = 0;
        await using var client = new GigaAmWorkerClient(() => new FakeConnection(++created == 1 ? hang ? "hang" : "crash" : "ok"),
            new WorkerRecoveryOptions { Backoff = TimeSpan.Zero, MinimumRequestTimeout = TimeSpan.FromMilliseconds(10), TimeoutPerAudioSecond = 0.001 });
        var failure = await Assert.ThrowsAsync<TranscriptionException>(async () => await client.TranscribeSegmentAsync(new float[1600], 16000, CancellationToken.None));
        Assert.Equal(hang ? TranscriptionErrorCode.WorkerTimeout : TranscriptionErrorCode.WorkerExited, failure.Code);
        Assert.Equal(WorkerState.Failed, client.State);
        Assert.Equal("ok", await client.TranscribeSegmentAsync(new float[1600], 16000, CancellationToken.None));
        Assert.Equal(2, created);
        Assert.Equal(WorkerState.Ready, client.State);
    }

    [Fact]
    public async Task PersistentCrashesStopAtRestartBudget()
    {
        var created = 0;
        await using var client = new GigaAmWorkerClient(() => { created++; return new FakeConnection("crash"); },
            new WorkerRecoveryOptions { MaximumRestarts = 1, Backoff = TimeSpan.Zero });
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<TranscriptionException>(async () => await client.TranscribeSegmentAsync(new float[1600], 16000, CancellationToken.None));
        var failure = await Assert.ThrowsAsync<TranscriptionException>(async () => await client.TranscribeSegmentAsync(new float[1600], 16000, CancellationToken.None));
        Assert.Equal(TranscriptionErrorCode.WorkerRestartLimit, failure.Code);
        Assert.Equal(2, created);
    }

    [Fact]
    public async Task CallerCancellationInvalidatesWithoutConsumingRestartBudget()
    {
        var created = 0;
        await using var client = new GigaAmWorkerClient(() => new FakeConnection(++created == 1 ? "hang" : "ok"),
            new WorkerRecoveryOptions { MaximumRestarts = 0, Backoff = TimeSpan.Zero });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.TranscribeSegmentAsync(new float[1600], 16000, cancel.Token));
        Assert.Equal("ok", await client.TranscribeSegmentAsync(new float[1600], 16000, CancellationToken.None));
    }

    [Fact]
    public async Task ProtocolFailureIsReportedWithoutReplay()
    {
        await using var client = new GigaAmWorkerClient(() => new FakeConnection("protocol"));
        var failure = await Assert.ThrowsAsync<TranscriptionException>(async () => await client.TranscribeSegmentAsync(new float[1600], 16000, CancellationToken.None));
        Assert.Equal(TranscriptionErrorCode.WorkerProtocol, failure.Code);
    }

    [Fact]
    public async Task ProtocolWritesCommandRateCountAndFloatPayload()
    {
        using var stream = new MemoryStream();
        await AsrWorkerProtocol.WriteRequestAsync(stream, new float[] { 0.5f, -1f }, 16000, CancellationToken.None);
        Assert.Equal(new byte[] { 1, 128, 62, 0, 0, 2, 0, 0, 0, 0, 0, 0, 63, 0, 0, 128, 191 }, stream.ToArray());
    }

    [Theory]
    [InlineData(new byte[] { 0, 2, 65 }, false)]
    [InlineData(new byte[] { 0, 255, 255, 255, 127 }, true)]
    [InlineData(new byte[] { 1, 1, 65 }, true)]
    [InlineData(new byte[] { 0, 1, 255 }, true)]
    public async Task ProtocolRejectsTruncationOversizeErrorAndInvalidUtf8(byte[] response, bool invalid)
    {
        using var stream = new MemoryStream(response);
        if (invalid) await Assert.ThrowsAsync<InvalidDataException>(async () => await AsrWorkerProtocol.ReadResponseAsync(stream, CancellationToken.None));
        else await Assert.ThrowsAsync<EndOfStreamException>(async () => await AsrWorkerProtocol.ReadResponseAsync(stream, CancellationToken.None));
    }

    private sealed class FakeConnection(string behavior) : IAsrWorkerConnection
    {
        public bool HasExited { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public async ValueTask<string> TranscribeAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken cancellationToken)
        {
            if (behavior == "hang") await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (behavior == "crash") { HasExited = true; throw new EndOfStreamException(); }
            if (behavior == "protocol") throw new InvalidDataException();
            return "ok";
        }
        public ValueTask DisposeAsync() { HasExited = true; return ValueTask.CompletedTask; }
    }
}

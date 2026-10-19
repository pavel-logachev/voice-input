using NAudio.Wave;
using VoiceInput.Windows.Audio;

namespace VoiceInput.Windows.Tests.Audio;

public sealed class WasapiPushToTalkRecorderTests
{
    // 16 kHz mono 16-bit PCM keeps the conversion trivial: no resampling, no channel mixing.
    private static readonly WaveFormat Format = new(16_000, 16, 1);

    [Fact]
    public async Task StopReturnsTheCapturedAudio()
    {
        var session = new FakeSession();
        using var recorder = new WasapiPushToTalkRecorder(() => session);
        await recorder.StartAsync(CancellationToken.None);
        session.Push(Pcm16(0.5f, 16_000));

        var audio = await recorder.StopAsync(CancellationToken.None);

        Assert.Equal(16_000, audio.SampleRate);
        Assert.Equal(16_000, audio.Samples.Length);
        Assert.Equal(0.5f, audio.Samples[0], precision: 2);
        Assert.True(session.StopCalled);
    }

    [Fact]
    public async Task MultipleChunksPreserveOrderAndOnlyRecordedBytes()
    {
        var session = new FakeSession();
        using var recorder = new WasapiPushToTalkRecorder(() => session);
        await recorder.StartAsync(CancellationToken.None);
        session.Push(Pcm16(0.25f, 731));
        session.Push(Pcm16(-0.5f, 17003));
        var audio = await recorder.StopAsync(CancellationToken.None);
        Assert.Equal(17734, audio.Samples.Length);
        Assert.All(audio.Samples[..731], value => Assert.Equal(0.25f, value, 3));
        Assert.All(audio.Samples[731..], value => Assert.Equal(-0.5f, value, 3));
        Assert.Equal(16000, audio.SampleRate);
    }

    [Fact]
    public async Task Stereo48KhzStillConvertsToMono16Khz()
    {
        var session = new FakeSession { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2) };
        using var recorder = new WasapiPushToTalkRecorder(() => session);
        await recorder.StartAsync(CancellationToken.None);
        var samples = new float[48000 * 2];
        for (var i = 0; i < samples.Length; i += 2) { samples[i] = 0.25f; samples[i + 1] = 0.75f; }
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        session.Push(bytes);
        var audio = await recorder.StopAsync(CancellationToken.None);
        Assert.Equal(16000, audio.SampleRate);
        Assert.InRange(audio.Samples.Length, 15900, 16100);
        Assert.Equal(0.5f, audio.Samples[8000], 2);
    }

    [Fact]
    public async Task StartingTwiceIsRefused()
    {
        using var recorder = new WasapiPushToTalkRecorder(() => new FakeSession());
        await recorder.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.StartAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task StoppingWithoutAnActiveRecordingIsRefused()
    {
        using var recorder = new WasapiPushToTalkRecorder(() => new FakeSession());

        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.StopAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task WaitForRecordingEndCompletesWhenTheDeviceStopsByItself()
    {
        var session = new FakeSession();
        using var recorder = new WasapiPushToTalkRecorder(() => session);
        await recorder.StartAsync(CancellationToken.None);
        var ended = recorder.WaitForRecordingEndAsync(CancellationToken.None);

        Assert.False(ended.IsCompleted);
        session.RaiseStopped(exception: null);

        await ended.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WaitForRecordingEndSurfacesADeviceFailure()
    {
        var session = new FakeSession();
        using var recorder = new WasapiPushToTalkRecorder(() => session);
        await recorder.StartAsync(CancellationToken.None);
        var ended = recorder.WaitForRecordingEndAsync(CancellationToken.None);

        session.RaiseStopped(new InvalidOperationException("device removed"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ended);
        Assert.Equal("device removed", error.Message);
    }

    [Fact]
    public async Task WaitForRecordingEndHonoursCancellation()
    {
        using var recorder = new WasapiPushToTalkRecorder(() => new FakeSession());
        await recorder.StartAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var ended = recorder.WaitForRecordingEndAsync(cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ended);
    }

    [Fact]
    public async Task CancelStopsAndReleasesTheDevice()
    {
        var session = new FakeSession();
        using var recorder = new WasapiPushToTalkRecorder(() => session);
        await recorder.StartAsync(CancellationToken.None);

        await recorder.CancelAsync();

        Assert.True(session.StopCalled);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task CancelWithoutARecordingIsHarmless()
    {
        using var recorder = new WasapiPushToTalkRecorder(() => new FakeSession());

        await recorder.CancelAsync();
    }

    [Fact]
    public async Task CaptureThatNeverStopsEndsInATimeout()
    {
        var session = new FakeSession { StopRaisesEvent = false };
        using var recorder = new WasapiPushToTalkRecorder(() => session);
        await recorder.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<TimeoutException>(() => recorder.StopAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task FailureToStartReleasesTheDeviceAndAllowsAnotherAttempt()
    {
        var failing = new FakeSession { StartFails = true };
        var healthy = new FakeSession();
        var sessions = new Queue<FakeSession>([failing, healthy]);
        using var recorder = new WasapiPushToTalkRecorder(() => sessions.Dequeue());

        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.StartAsync(CancellationToken.None).AsTask());
        await WaitUntilAsync(() => failing.Disposed);

        await recorder.StartAsync(CancellationToken.None);
        Assert.False(healthy.Disposed);
    }

    [Fact]
    public async Task NewRecordingWaitsUntilThePreviousDeviceIsReleased()
    {
        var first = new FakeSession { DisposeGate = new ManualResetEventSlim(false) };
        var second = new FakeSession();
        var sessions = new Queue<FakeSession>([first, second]);
        using var recorder = new WasapiPushToTalkRecorder(() => sessions.Dequeue());
        await recorder.StartAsync(CancellationToken.None);
        first.Push(Pcm16(0.1f, 1_600));
        var stopping = recorder.StopAsync(CancellationToken.None).AsTask();
        await WaitUntilAsync(() => first.DisposeStarted);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => recorder.StartAsync(CancellationToken.None).AsTask());

        Assert.Contains("еще завершает предыдущую запись", error.Message, StringComparison.Ordinal);
        first.DisposeGate.Set();
        await stopping;
    }

    [Fact]
    public async Task RecordingStopsByItselfAtTheMaximumDuration()
    {
        var session = new FakeSession();
        using var recorder = new WasapiPushToTalkRecorder(() => session);
        await recorder.StartAsync(CancellationToken.None);
        var ended = recorder.WaitForRecordingEndAsync(CancellationToken.None);
        var limitBytes = (long)(Format.AverageBytesPerSecond * WasapiPushToTalkRecorder.MaximumRecordingDuration.TotalSeconds);

        // Deliver slightly more than the limit in chunks; the recorder must stop the capture on its own.
        var chunk = new byte[(int)(limitBytes / 4) + 1];
        for (var index = 0; index < 4; index++)
        {
            session.Push(chunk);
        }

        await ended.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(session.StopCalled);
    }

    [Fact]
    public async Task LevelIsPublishedWhileRecording()
    {
        var session = new FakeSession();
        using var recorder = new WasapiPushToTalkRecorder(() => session);
        var levels = new List<float>();
        recorder.RecordingLevelChanged += levels.Add;
        await recorder.StartAsync(CancellationToken.None);

        session.Push(Pcm16(0.6f, 3_200));

        Assert.NotEmpty(levels);
        Assert.All(levels, level => Assert.InRange(level, 0f, 1f));
    }

    [Fact]
    public async Task AFailingLevelListenerNeverInterruptsCapture()
    {
        var session = new FakeSession();
        using var recorder = new WasapiPushToTalkRecorder(() => session);
        recorder.RecordingLevelChanged += _ => throw new InvalidOperationException("meter broke");
        await recorder.StartAsync(CancellationToken.None);
        session.Push(Pcm16(0.6f, 3_200));

        var audio = await recorder.StopAsync(CancellationToken.None);

        Assert.Equal(3_200, audio.Samples.Length);
    }

    [Fact]
    public async Task DisposedRecorderRefusesToStart()
    {
        var recorder = new WasapiPushToTalkRecorder(() => new FakeSession());
        recorder.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => recorder.StartAsync(CancellationToken.None).AsTask());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeDoesNotWaitForBlockedStartupAndLateDeviceIsDisposedExactlyOnce(bool blockFactory)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var session = new FakeSession { StartEntered = blockFactory ? null : entered, StartGate = blockFactory ? null : release };
        var recorder = new WasapiPushToTalkRecorder(() =>
        {
            if (blockFactory) { entered.Set(); release.Wait(); }
            return session;
        });
        var start = recorder.StartAsync(CancellationToken.None).AsTask();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
        var disposal = Task.Run(recorder.Dispose);
        try
        {
            await disposal.WaitAsync(TimeSpan.FromMilliseconds(2500));
            Assert.False(session.Disposed);
        }
        finally
        {
            release.Set();
            await start.WaitAsync(TimeSpan.FromSeconds(3));
            await disposal.WaitAsync(TimeSpan.FromSeconds(3));
        }
        await WaitUntilAsync(() => session.Disposed);
        recorder.Dispose();
        Assert.Equal(1, session.DisposeCalls);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => recorder.StartAsync(CancellationToken.None).AsTask());
    }

    private static byte[] Pcm16(float value, int samples)
    {
        var bytes = new byte[samples * 2];
        var sample = (short)(value * short.MaxValue);
        for (var index = 0; index < samples; index++)
        {
            bytes[index * 2] = (byte)(sample & 0xFF);
            bytes[(index * 2) + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return bytes;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeSession : IAudioCaptureSession
    {
        private int stopCalls;

        public WaveFormat WaveFormat { get; init; } = Format;

        public bool StopRaisesEvent { get; init; } = true;

        public bool StartFails { get; init; }
        public ManualResetEventSlim? StartEntered { get; init; }
        public ManualResetEventSlim? StartGate { get; init; }
        public int DisposeCalls { get; private set; }

        public ManualResetEventSlim? DisposeGate { get; init; }

        public bool StopCalled => Volatile.Read(ref stopCalls) > 0;

        public bool Disposed { get; private set; }

        public bool DisposeStarted { get; private set; }

        public event EventHandler<WaveInEventArgs>? DataAvailable;

        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public void StartRecording()
        {
            StartEntered?.Set();
            StartGate?.Wait();
            if (StartFails)
            {
                throw new InvalidOperationException("microphone unavailable");
            }
        }

        public void StopRecording()
        {
            Interlocked.Increment(ref stopCalls);
            if (StopRaisesEvent)
            {
                RaiseStopped(exception: null);
            }
        }

        public void Push(byte[] bytes) => DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, bytes.Length));

        public void RaiseStopped(Exception? exception) => RecordingStopped?.Invoke(this, new StoppedEventArgs(exception));

        public void Dispose()
        {
            DisposeCalls++;
            DisposeStarted = true;
            DisposeGate?.Wait(TimeSpan.FromSeconds(10));
            Disposed = true;
        }
    }
}

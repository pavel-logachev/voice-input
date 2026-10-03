using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceInput.Core.Audio;

namespace VoiceInput.Windows.Audio;

public sealed class WasapiPushToTalkRecorder : IAudioRecorder, IRecordingLevelSource, IDisposable
{
    private const int OutputSampleRate = 16_000;
    private static readonly TimeSpan StopWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly object gate = new();
    private readonly Func<IAudioCaptureSession> captureFactory;
    private IAudioCaptureSession? capture;
    private MemoryStream? rawAudio;
    private WaveFormat? capturedFormat;
    private BufferedWaveProvider? levelWaveBuffer;
    private ISampleProvider? levelSampleProvider;
    private float[]? levelSamples;
    private TaskCompletionSource? recordingStopped;
    private Task cleanupTask = Task.CompletedTask;
    private long maximumRawBytes;
    private bool stopRequested;
    private bool disposed;

    public WasapiPushToTalkRecorder()
        : this(static () => new WasapiCaptureSession())
    {
    }

    /// <summary>Records from the microphone the delegate names at the moment each recording starts (null: Windows default).</summary>
    public WasapiPushToTalkRecorder(Func<string?> microphoneId)
        : this(() => new WasapiCaptureSession((microphoneId ?? throw new ArgumentNullException(nameof(microphoneId)))()))
    {
    }

    internal WasapiPushToTalkRecorder(Func<IAudioCaptureSession> captureFactory)
    {
        this.captureFactory = captureFactory ?? throw new ArgumentNullException(nameof(captureFactory));
    }

    public event Action<float>? RecordingLevelChanged;

    /// <summary>The longest single recording; the capture stops by itself when it is reached.</summary>
    public static TimeSpan MaximumRecordingDuration { get; } = TimeSpan.FromMinutes(10);

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Run(
            () =>
            {
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    if (capture is not null)
                    {
                        throw new InvalidOperationException("Audio recording is already active.");
                    }

                    if (!cleanupTask.IsCompleted)
                    {
                        throw new InvalidOperationException(
                            "Микрофон ещё завершает предыдущую запись. Повторите через несколько секунд.");
                    }

                    var session = capture = captureFactory();
                    try
                    {
                        capturedFormat = session.WaveFormat;
                        maximumRawBytes = checked((long)(capturedFormat.AverageBytesPerSecond * MaximumRecordingDuration.TotalSeconds));
                        rawAudio = new MemoryStream(capacity: (int)Math.Min(maximumRawBytes, 4 * 1024 * 1024));
                        recordingStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        Observe(recordingStopped.Task);
                        stopRequested = false;
                        InitializeLevelTracking(capturedFormat);

                        session.DataAvailable += OnDataAvailable;
                        session.RecordingStopped += OnRecordingStopped;
                        session.StartRecording();
                    }
                    catch
                    {
                        CleanupRecording(session);
                        throw;
                    }
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RecordedAudio> StopAsync(CancellationToken cancellationToken)
    {
        IAudioCaptureSession currentCapture;
        Task stoppedTask;
        lock (gate)
        {
            currentCapture = capture ?? throw new InvalidOperationException("Audio recording is not active.");
            stoppedTask = recordingStopped?.Task ?? throw new InvalidOperationException("The recording completion signal is missing.");
        }

        RequestStop(currentCapture);
        await stoppedTask.WaitAsync(StopWaitTimeout, cancellationToken).ConfigureAwait(false);

        byte[] bytes;
        WaveFormat format;
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bytes = rawAudio?.ToArray() ?? [];
            format = capturedFormat ?? throw new InvalidOperationException("The capture format was not available.");
        }

        await CleanupRecording(currentCapture).WaitAsync(StopWaitTimeout, cancellationToken).ConfigureAwait(false);
        return await Task.Run(
            () => new RecordedAudio(ConvertToMono16Khz(bytes, format), OutputSampleRate),
            cancellationToken).ConfigureAwait(false);
    }

    public Task WaitForRecordingEndAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return recordingStopped?.Task.WaitAsync(cancellationToken) ?? Task.CompletedTask;
        }
    }

    public async ValueTask CancelAsync()
    {
        IAudioCaptureSession? currentCapture;
        Task? stoppedTask;
        lock (gate)
        {
            currentCapture = capture;
            stoppedTask = recordingStopped?.Task;
        }

        if (currentCapture is null)
        {
            return;
        }

        RequestStop(currentCapture);
        if (stoppedTask is not null)
        {
            try
            {
                await stoppedTask.WaitAsync(StopWaitTimeout).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Cancellation is best-effort; the original workflow error remains authoritative.
            }
        }

        await CleanupRecording(currentCapture).WaitAsync(StopWaitTimeout).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        try
        {
            CancelAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Shutting down: nothing useful can be done with a late audio error.
        }

        GC.SuppressFinalize(this);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        float? level = null;
        IAudioCaptureSession? limitStop = null;
        lock (gate)
        {
            if (!ReferenceEquals(sender, capture) || rawAudio is null)
            {
                return;
            }

            if (rawAudio.Length < maximumRawBytes)
            {
                var bytesToWrite = (int)Math.Min(maximumRawBytes - rawAudio.Length, eventArgs.BytesRecorded);
                rawAudio.Write(eventArgs.Buffer, 0, bytesToWrite);
                if (rawAudio.Length >= maximumRawBytes)
                {
                    limitStop = capture;
                }

                if (levelWaveBuffer is not null && levelSampleProvider is not null && levelSamples is not null)
                {
                    levelWaveBuffer.AddSamples(eventArgs.Buffer, 0, bytesToWrite);
                    var sampleCount = levelSampleProvider.Read(levelSamples);
                    if (sampleCount > 0)
                    {
                        level = AudioLevelNormalizer.FromSamples(levelSamples.AsSpan(0, sampleCount));
                    }
                }
            }
        }

        if (limitStop is not null)
        {
            // Stop outside the device callback and outside the lock.
            _ = Task.Run(() => RequestStop(limitStop));
        }

        if (level.HasValue)
        {
            PublishLevel(level.Value);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        lock (gate)
        {
            if (!ReferenceEquals(sender, capture) || recordingStopped is null)
            {
                return;
            }

            if (eventArgs.Exception is null)
            {
                recordingStopped.TrySetResult();
            }
            else
            {
                recordingStopped.TrySetException(eventArgs.Exception);
            }
        }
    }

    private void RequestStop(IAudioCaptureSession session)
    {
        TaskCompletionSource? completion;
        lock (gate)
        {
            if (!ReferenceEquals(capture, session) || stopRequested)
            {
                return;
            }

            stopRequested = true;
            completion = recordingStopped;
        }

        try
        {
            session.StopRecording();
        }
        catch (Exception exception)
        {
            completion?.TrySetException(exception);
        }
    }

    // Disposing a capture device can block, so it runs off the caller's thread. A new recording
    // cannot start until the previous device is released.
    private Task CleanupRecording(IAudioCaptureSession recording)
    {
        lock (gate)
        {
            if (!ReferenceEquals(capture, recording))
            {
                return cleanupTask;
            }

            recording.DataAvailable -= OnDataAvailable;
            recording.RecordingStopped -= OnRecordingStopped;
            var buffer = rawAudio;
            capture = null;
            rawAudio = null;
            capturedFormat = null;
            levelWaveBuffer = null;
            levelSampleProvider = null;
            levelSamples = null;
            recordingStopped = null;
            maximumRawBytes = 0;
            stopRequested = false;
            cleanupTask = Task.Run(() =>
            {
                try
                {
                    recording.Dispose();
                }
                finally
                {
                    buffer?.Dispose();
                }
            });
            Observe(cleanupTask);
            return cleanupTask;
        }
    }

    private static void Observe(Task task) =>
        task.ContinueWith(
            static completed => completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private void InitializeLevelTracking(WaveFormat format)
    {
        try
        {
            levelWaveBuffer = new BufferedWaveProvider(format, TimeSpan.FromSeconds(1))
            {
                DiscardOnBufferOverflow = true,
                ReadFully = false,
            };
            levelSampleProvider = levelWaveBuffer.ToSampleProvider();
            levelSamples = new float[Math.Max(4_096, format.SampleRate * format.Channels)];
        }
        catch (NotSupportedException)
        {
            levelWaveBuffer = null;
            levelSampleProvider = null;
            levelSamples = null;
        }
    }

    private void PublishLevel(float level)
    {
        try
        {
            RecordingLevelChanged?.Invoke(level);
        }
        catch (Exception)
        {
            // A visual meter must never interrupt microphone capture.
        }
    }

    private static float[] ConvertToMono16Khz(byte[] bytes, WaveFormat format)
    {
        if (bytes.Length == 0)
        {
            return [];
        }

        using var memory = new MemoryStream(bytes, writable: false);
        using var raw = new RawSourceWaveStream(memory, format);
        ISampleProvider provider = raw.ToSampleProvider();

        if (provider.WaveFormat.Channels != 1)
        {
            provider = new MonoMixingSampleProvider(provider);
        }

        if (provider.WaveFormat.SampleRate != OutputSampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, OutputSampleRate);
        }

        var result = new List<float>();
        var buffer = new float[OutputSampleRate];
        int read;
        while ((read = provider.Read(buffer)) > 0)
        {
            result.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        return [.. result];
    }
}

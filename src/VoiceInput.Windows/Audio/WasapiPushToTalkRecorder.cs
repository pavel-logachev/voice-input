using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceInput.Core.Audio;

namespace VoiceInput.Windows.Audio;

public sealed class WasapiPushToTalkRecorder : IAudioRecorder, IRecordingLevelSource, IDisposable
{
    private const int OutputSampleRate = 16_000;
    private static readonly TimeSpan MaximumRecording = TimeSpan.FromMinutes(2);

    private readonly object gate = new();
    private WasapiCapture? capture;
    private MemoryStream? rawAudio;
    private WaveFormat? capturedFormat;
    private BufferedWaveProvider? levelWaveBuffer;
    private ISampleProvider? levelSampleProvider;
    private float[]? levelSamples;
    private TaskCompletionSource? recordingStopped;
    private long maximumRawBytes;
    private bool disposed;

    public event Action<float>? RecordingLevelChanged;

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Run(
            () =>
            {
                lock (gate)
                {
                    if (capture is not null)
                    {
                        throw new InvalidOperationException("Audio recording is already active.");
                    }

                    var nextCapture = new WasapiCapture();
                    capturedFormat = nextCapture.WaveFormat;
                    maximumRawBytes = checked((long)(capturedFormat.AverageBytesPerSecond * MaximumRecording.TotalSeconds));
                    rawAudio = new MemoryStream(capacity: (int)Math.Min(maximumRawBytes, 4 * 1024 * 1024));
                    recordingStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    InitializeLevelTracking(capturedFormat);

                    nextCapture.DataAvailable += OnDataAvailable;
                    nextCapture.RecordingStopped += OnRecordingStopped;
                    capture = nextCapture;

                    try
                    {
                        nextCapture.StartRecording();
                    }
                    catch
                    {
                        CleanupRecording(nextCapture);
                        throw;
                    }
                }
            },
            cancellationToken);
    }

    public async ValueTask<RecordedAudio> StopAsync(CancellationToken cancellationToken)
    {
        var (currentCapture, stoppedTask) = GetActiveRecording();
        currentCapture.StopRecording();
        await stoppedTask.WaitAsync(cancellationToken);

        byte[] bytes;
        WaveFormat format;
        lock (gate)
        {
            bytes = rawAudio?.ToArray() ?? [];
            format = capturedFormat ?? throw new InvalidOperationException("The capture format was not available.");
            CleanupRecording(currentCapture);
        }

        var samples = ConvertToMono16Khz(bytes, format);
        return new RecordedAudio(samples, OutputSampleRate);
    }

    public async ValueTask CancelAsync()
    {
        WasapiCapture? currentCapture;
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

        currentCapture.StopRecording();
        if (stoppedTask is not null)
        {
            try
            {
                await stoppedTask;
            }
            catch
            {
                // Cancellation is best-effort; the original workflow error remains authoritative.
            }
        }

        lock (gate)
        {
            CleanupRecording(currentCapture);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancelAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        float? level = null;
        lock (gate)
        {
            if (!ReferenceEquals(sender, capture) || rawAudio is null)
            {
                return;
            }

            var remaining = maximumRawBytes - rawAudio.Length;
            if (remaining <= 0)
            {
                return;
            }

            var bytesToWrite = (int)Math.Min(remaining, eventArgs.BytesRecorded);
            rawAudio.Write(eventArgs.Buffer, 0, bytesToWrite);

            if (levelWaveBuffer is not null && levelSampleProvider is not null && levelSamples is not null)
            {
                levelWaveBuffer.AddSamples(eventArgs.Buffer, 0, bytesToWrite);
                var sampleCount = levelSampleProvider.Read(levelSamples, 0, levelSamples.Length);
                if (sampleCount > 0)
                {
                    level = AudioLevelNormalizer.FromSamples(levelSamples.AsSpan(0, sampleCount));
                }
            }
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

    private (WasapiCapture Capture, Task StoppedTask) GetActiveRecording()
    {
        lock (gate)
        {
            return (
                capture ?? throw new InvalidOperationException("Audio recording is not active."),
                recordingStopped?.Task ?? throw new InvalidOperationException("The recording completion signal is missing."));
        }
    }

    private void CleanupRecording(WasapiCapture recording)
    {
        recording.DataAvailable -= OnDataAvailable;
        recording.RecordingStopped -= OnRecordingStopped;
        recording.Dispose();
        rawAudio?.Dispose();

        if (ReferenceEquals(capture, recording))
        {
            capture = null;
            rawAudio = null;
            capturedFormat = null;
            levelWaveBuffer = null;
            levelSampleProvider = null;
            levelSamples = null;
            recordingStopped = null;
            maximumRawBytes = 0;
        }
    }

    private void InitializeLevelTracking(WaveFormat format)
    {
        try
        {
            levelWaveBuffer = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromSeconds(1),
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
        catch
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
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            result.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        return result.ToArray();
    }
}

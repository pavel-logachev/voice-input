using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceInput.Windows.Audio;

internal sealed class WasapiCaptureSession : IAudioCaptureSession
{
    // Opening the device normally takes a few milliseconds; the bound only protects against a stuck driver.
    private static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(2);

    // WASAPI hands audio over in packets of about 10 ms. They are passed on in chunks of about 50 ms,
    // which keeps the level meter and the recorder at the pace they were tuned for.
    private const int ChunksPerSecond = 20;

    private readonly MMDevice device;
    private readonly WasapiRecorder capture;
    private readonly int chunkBytes;
    private byte[] chunk;
    private int chunkLength;

    public WasapiCaptureSession()
        : this(deviceId: null)
    {
    }

    /// <summary>Captures from the given endpoint id, falling back to the Windows default device.</summary>
    public WasapiCaptureSession(string? deviceId)
    {
        device = AudioDeviceCatalog.OpenCaptureDevice(deviceId, out _);
        try
        {
            // Polling in shared mode with the default 100 ms buffer: the same cadence as the previous capture path.
            capture = new WasapiRecorderBuilder().WithDevice(device).WithSharedMode().WithPollingSync().Build();
        }
        catch
        {
            device.Dispose();
            throw;
        }

        // The device mix format arrives as WAVEFORMATEXTENSIBLE; the recorder works with the plain PCM/float form.
        WaveFormat = capture.WaveFormat.AsStandardWaveFormat();
        chunkBytes = Math.Max(WaveFormat.BlockAlign, WaveFormat.AverageBytesPerSecond / ChunksPerSecond);
        chunk = new byte[chunkBytes * 2];
        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
    }

    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public WaveFormat WaveFormat { get; }

    public void StartRecording() => capture.StartRecording();

    public void StopRecording()
    {
        // The capture thread marks itself as capturing once the device has started. A stop requested
        // before that point would be overwritten and the recording would never end, so wait for the
        // startup to settle first (a very short key press gets here that early).
        SpinWait.SpinUntil(() => capture.CaptureState != CaptureState.Starting, StartupWait);
        capture.StopRecording();
    }

    public void Dispose()
    {
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        StopRecording();
        capture.Dispose();
        device.Dispose();
    }

    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        // The span points into the WASAPI buffer and is valid only during this callback, so it is copied.
        // Packets arrive one at a time on the capture thread: no locking is needed for the chunk.
        if (chunk.Length < chunkLength + buffer.Length)
        {
            Array.Resize(ref chunk, Math.Max(chunk.Length * 2, chunkLength + buffer.Length));
        }

        buffer.CopyTo(chunk.AsSpan(chunkLength));
        chunkLength += buffer.Length;
        if (chunkLength >= chunkBytes)
        {
            FlushChunk();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // The tail shorter than one chunk still belongs to the recording.
        FlushChunk();
        RecordingStopped?.Invoke(this, e);
    }

    private void FlushChunk()
    {
        var length = chunkLength;
        if (length == 0)
        {
            return;
        }

        chunkLength = 0;
        DataAvailable?.Invoke(this, new WaveInEventArgs(chunk, length));
    }
}

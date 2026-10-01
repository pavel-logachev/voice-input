using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceInput.Windows.Audio;

internal sealed class WasapiCaptureSession : IAudioCaptureSession
{
    private readonly IWaveIn capture;

    public WasapiCaptureSession()
        : this(new WasapiCapture())
    {
    }

    internal WasapiCaptureSession(IWaveIn capture)
    {
        this.capture = capture;
        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
    }

    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public WaveFormat WaveFormat => capture.WaveFormat;

    public void StartRecording() => capture.StartRecording();

    public void StopRecording() => capture.StopRecording();

    public void Dispose()
    {
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        capture.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e) => DataAvailable?.Invoke(this, e);

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) => RecordingStopped?.Invoke(this, e);
}

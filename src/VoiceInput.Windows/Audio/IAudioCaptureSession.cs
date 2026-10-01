using NAudio.Wave;

namespace VoiceInput.Windows.Audio;

/// <summary>
/// A single microphone capture. Abstracted so the recorder's stop, timeout and cleanup rules
/// can be tested without audio hardware.
/// </summary>
internal interface IAudioCaptureSession : IDisposable
{
    WaveFormat WaveFormat { get; }

    event EventHandler<WaveInEventArgs>? DataAvailable;

    event EventHandler<StoppedEventArgs>? RecordingStopped;

    void StartRecording();

    void StopRecording();
}

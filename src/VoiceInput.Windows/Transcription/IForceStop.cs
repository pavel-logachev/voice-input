namespace VoiceInput.Windows.Transcription;

/// <summary>Last-resort shutdown of only the worker owned by this engine; never waits for a request.</summary>
public interface IForceStop
{
    void ForceStop();
}

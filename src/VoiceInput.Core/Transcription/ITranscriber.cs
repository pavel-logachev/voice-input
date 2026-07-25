using VoiceInput.Core.Audio;

namespace VoiceInput.Core.Transcription;

public interface ITranscriber
{
    ValueTask<string> TranscribeAsync(RecordedAudio audio, CancellationToken cancellationToken);
}

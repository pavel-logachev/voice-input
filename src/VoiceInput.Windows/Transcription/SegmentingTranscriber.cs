using VoiceInput.Core.Audio;
using VoiceInput.Core.Transcription;

namespace VoiceInput.Windows.Transcription;

public interface IAsrSegmentClient
{
    ValueTask<string> TranscribeSegmentAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken);
}

public sealed class SegmentingTranscriber(
    IAsrSegmentClient client,
    PcmSegmentationOptions? segmentationOptions = null) : ITranscriber
{
    private readonly PcmSegmentationOptions options = segmentationOptions ?? new PcmSegmentationOptions();

    public async ValueTask<string> TranscribeAsync(RecordedAudio audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.SampleRate != options.SampleRate)
        {
            throw new ArgumentException(
                $"Expected {options.SampleRate} Hz audio, received {audio.SampleRate} Hz.",
                nameof(audio));
        }

        var minimumSamples = Math.Max(1, audio.SampleRate / 10);
        if (audio.Samples.Length < minimumSamples)
        {
            return string.Empty;
        }

        var transcripts = new List<string>();
        foreach (var segment in PcmSegmenter.Split(audio.Samples, options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = (await client.TranscribeSegmentAsync(segment, audio.SampleRate, cancellationToken)).Trim();
            if (text.Length > 0)
            {
                transcripts.Add(text);
            }
        }

        return string.Join(' ', transcripts);
    }
}

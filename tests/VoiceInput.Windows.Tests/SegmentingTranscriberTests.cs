using VoiceInput.Core.Audio;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests.Transcription;

public sealed class SegmentingTranscriberTests
{
    [Fact]
    public async Task TranscriberSplitsLongAudioAndJoinsNonEmptySegmentText()
    {
        var samples = Enumerable.Repeat(1f, 120).ToArray();
        Array.Fill(samples, 0f, 44, 10);
        Array.Fill(samples, 0f, 94, 10);
        var client = new RecordingSegmentClient(["Первый.", "", "Третий."]);
        var transcriber = new SegmentingTranscriber(
            client,
            new PcmSegmentationOptions(
                SampleRate: 10,
                MaximumSegment: TimeSpan.FromSeconds(6),
                SearchBack: TimeSpan.FromSeconds(2),
                AnalysisWindow: TimeSpan.FromSeconds(1)));

        var text = await transcriber.TranscribeAsync(new RecordedAudio(samples, 10), CancellationToken.None);

        Assert.Equal("Первый. Третий.", text);
        Assert.Equal([50, 50, 20], client.SegmentLengths);
    }

    private sealed class RecordingSegmentClient(IEnumerable<string> responses) : IAsrSegmentClient
    {
        private readonly Queue<string> responseQueue = new(responses);

        public List<int> SegmentLengths { get; } = [];

        public ValueTask<string> TranscribeSegmentAsync(
            ReadOnlyMemory<float> samples,
            int sampleRate,
            CancellationToken cancellationToken)
        {
            SegmentLengths.Add(samples.Length);
            return ValueTask.FromResult(responseQueue.Dequeue());
        }
    }
}

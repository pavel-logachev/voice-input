using VoiceInput.Core.Audio;

namespace VoiceInput.Core.Tests.Audio;

public sealed class PcmSegmenterTests
{
    [Fact]
    public void LongAudioSplitsAtQuietestWindowsBeforeMaximumLength()
    {
        var samples = Enumerable.Repeat(1f, 120).ToArray();
        Array.Fill(samples, 0f, 44, 10);
        Array.Fill(samples, 0f, 94, 10);

        var segments = PcmSegmenter.Split(
            samples,
            new PcmSegmentationOptions(
                SampleRate: 10,
                MaximumSegment: TimeSpan.FromSeconds(6),
                SearchBack: TimeSpan.FromSeconds(2),
                AnalysisWindow: TimeSpan.FromSeconds(1)));

        Assert.Equal([50, 50, 20], segments.Select(segment => segment.Length));
        Assert.Equal(samples, segments.SelectMany(segment => segment.ToArray()));
        Assert.All(segments, segment => Assert.InRange(segment.Length, 1, 60));
    }
}

using VoiceInput.Core.Audio;

namespace VoiceInput.Core.Tests.Audio;

public sealed class AudioLevelNormalizerTests
{
    [Fact]
    public void EmptyAndSilentSamplesProduceFlatLevel()
    {
        Assert.Equal(0, AudioLevelNormalizer.FromSamples([]));
        Assert.Equal(0, AudioLevelNormalizer.FromSamples([0f, 0f, 0f]));
        Assert.Equal(0, AudioLevelNormalizer.FromSamples([0.001f, -0.001f]));
    }

    [Fact]
    public void SpeechLevelProducesVisibleNormalizedValue()
    {
        var level = AudioLevelNormalizer.FromSamples([0.1f, -0.1f, 0.1f, -0.1f]);

        Assert.InRange(level, 0.7f, 1f);
    }

    [Fact]
    public void LevelIsClampedForOutOfRangeSamples()
    {
        var level = AudioLevelNormalizer.FromSamples([2f, -2f]);

        Assert.Equal(1f, level);
    }
}

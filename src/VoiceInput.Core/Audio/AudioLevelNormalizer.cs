namespace VoiceInput.Core.Audio;

public static class AudioLevelNormalizer
{
    private const double NoiseFloorDecibels = -52;
    private const double FullLevelDecibels = -14;

    public static float FromSamples(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }

        double sumOfSquares = 0;
        var finiteSampleCount = 0;
        foreach (var sample in samples)
        {
            if (!float.IsFinite(sample))
            {
                continue;
            }

            sumOfSquares += sample * sample;
            finiteSampleCount++;
        }

        if (finiteSampleCount == 0 || sumOfSquares <= 0)
        {
            return 0;
        }

        var rms = Math.Sqrt(sumOfSquares / finiteSampleCount);
        var decibels = 20 * Math.Log10(rms);
        var normalized = (decibels - NoiseFloorDecibels) /
            (FullLevelDecibels - NoiseFloorDecibels);
        return (float)Math.Clamp(normalized, 0, 1);
    }
}

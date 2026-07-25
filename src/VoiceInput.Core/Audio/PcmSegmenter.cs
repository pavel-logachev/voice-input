namespace VoiceInput.Core.Audio;

public sealed record PcmSegmentationOptions(
    int SampleRate = 16_000,
    TimeSpan? MaximumSegment = null,
    TimeSpan? SearchBack = null,
    TimeSpan? AnalysisWindow = null)
{
    public TimeSpan EffectiveMaximumSegment => MaximumSegment ?? TimeSpan.FromSeconds(20);

    public TimeSpan EffectiveSearchBack => SearchBack ?? TimeSpan.FromSeconds(5);

    public TimeSpan EffectiveAnalysisWindow => AnalysisWindow ?? TimeSpan.FromMilliseconds(200);
}

public static class PcmSegmenter
{
    public static IReadOnlyList<ReadOnlyMemory<float>> Split(
        float[] samples,
        PcmSegmentationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        options ??= new PcmSegmentationOptions();

        var maximumSamples = ToSamples(options.EffectiveMaximumSegment, options.SampleRate);
        var searchBackSamples = ToSamples(options.EffectiveSearchBack, options.SampleRate);
        var analysisSamples = ToSamples(options.EffectiveAnalysisWindow, options.SampleRate);

        if (maximumSamples <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum segment duration must be positive.");
        }

        if (searchBackSamples <= 0 || searchBackSamples >= maximumSamples)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Search-back duration must be positive and shorter than the maximum segment.");
        }

        if (analysisSamples <= 0 || analysisSamples > searchBackSamples)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Analysis window must be positive and no longer than the search-back duration.");
        }

        if (samples.Length == 0)
        {
            return [];
        }

        var segments = new List<ReadOnlyMemory<float>>();
        var offset = 0;

        while (samples.Length - offset > maximumSamples)
        {
            var maximumCut = offset + maximumSamples;
            var searchStart = maximumCut - searchBackSamples;
            var cut = FindQuietestWindowCenter(samples, searchStart, maximumCut, analysisSamples);

            if (cut <= offset || cut > maximumCut)
            {
                cut = maximumCut;
            }

            segments.Add(samples.AsMemory(offset, cut - offset));
            offset = cut;
        }

        if (offset < samples.Length)
        {
            segments.Add(samples.AsMemory(offset));
        }

        return segments;
    }

    private static int FindQuietestWindowCenter(float[] samples, int start, int end, int windowLength)
    {
        var halfWindow = windowLength / 2;
        var firstCenter = start + halfWindow;
        var lastCenter = end - (windowLength - halfWindow);
        var step = Math.Max(1, windowLength / 2);
        var bestCenter = end;
        var bestEnergy = double.PositiveInfinity;

        for (var center = firstCenter; center <= lastCenter; center += step)
        {
            var windowStart = center - halfWindow;
            var energy = 0d;
            for (var index = windowStart; index < windowStart + windowLength; index++)
            {
                var sample = samples[index];
                energy += sample * sample;
            }

            if (energy < bestEnergy)
            {
                bestEnergy = energy;
                bestCenter = center;
            }
        }

        return bestCenter;
    }

    private static int ToSamples(TimeSpan duration, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return checked((int)Math.Round(duration.TotalSeconds * sampleRate));
    }
}

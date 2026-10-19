namespace VoiceInput.Core.Audio;

/// <summary>A UI-clocked envelope and fixed-time history, independent of audio-buffer frequency.</summary>
public sealed class RecordingLevelHistory
{
    private const float SilenceThreshold = 0.015f;
    private const double SampleSeconds = 0.030;
    private const double AttackSeconds = 0.025;
    private const double ReleaseSeconds = 0.080;
    private readonly float[] values;
    private float smoothedLevel;
    private double sampleElapsed;

    public RecordingLevelHistory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        values = new float[capacity];
    }

    public ReadOnlySpan<float> Values => values;
    public float CurrentLevel => smoothedLevel;
    public double ScrollFraction => sampleElapsed / SampleSeconds;

    // Kept for callers supplying a fixed-rate history (including still previews).
    public void Push(float level) => Advance(level, TimeSpan.FromSeconds(SampleSeconds));

    public void Advance(float level, TimeSpan elapsed)
    {
        level = float.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        var remaining = elapsed.TotalSeconds;
        if (remaining <= 0) return;
        // A stalled/hidden UI must not replay seconds of obsolete audio.
        remaining = Math.Min(remaining, SampleSeconds * values.Length);
        while (remaining > 1e-9)
        {
            var step = Math.Min(remaining, SampleSeconds - sampleElapsed);
            var timeConstant = level > smoothedLevel ? AttackSeconds : ReleaseSeconds;
            smoothedLevel = (float)(level + (smoothedLevel - level) * Math.Exp(-step / timeConstant));
            if (level == 0 && smoothedLevel < SilenceThreshold) smoothedLevel = 0;
            sampleElapsed += step;
            remaining -= step;
            if (sampleElapsed >= SampleSeconds - 1e-9)
            {
                Array.Copy(values, 1, values, 0, values.Length - 1);
                values[^1] = smoothedLevel;
                sampleElapsed = 0;
            }
        }
    }

    public void Reset()
    {
        smoothedLevel = 0;
        sampleElapsed = 0;
        Array.Clear(values);
    }
}

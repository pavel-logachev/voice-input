namespace VoiceInput.Core.Audio;

public sealed class RecordingLevelHistory
{
    private const float SilenceThreshold = 0.015f;
    private const float ReleaseFactor = 0.65f;

    private readonly float[] values;
    private float smoothedLevel;

    public RecordingLevelHistory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        values = new float[capacity];
    }

    public ReadOnlySpan<float> Values => values;

    public void Push(float level)
    {
        level = Math.Clamp(level, 0, 1);
        smoothedLevel = level >= smoothedLevel
            ? level
            : smoothedLevel * ReleaseFactor;
        if (smoothedLevel < SilenceThreshold)
        {
            smoothedLevel = 0;
        }

        Array.Copy(values, 1, values, 0, values.Length - 1);
        values[^1] = smoothedLevel;
    }

    public void Reset()
    {
        smoothedLevel = 0;
        Array.Clear(values);
    }
}

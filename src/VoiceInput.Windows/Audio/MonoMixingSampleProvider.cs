using System.Buffers;
using NAudio.Wave;

namespace VoiceInput.Windows.Audio;

public sealed class MonoMixingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int sourceChannels;

    public MonoMixingSampleProvider(ISampleProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.source = source;
        sourceChannels = source.WaveFormat.Channels;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceChannels);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var requestedSourceSamples = checked(count * sourceChannels);
        var sourceBuffer = ArrayPool<float>.Shared.Rent(requestedSourceSamples);
        try
        {
            var sourceSamplesRead = source.Read(sourceBuffer, 0, requestedSourceSamples);
            var framesRead = sourceSamplesRead / sourceChannels;

            for (var frame = 0; frame < framesRead; frame++)
            {
                var sum = 0f;
                var sourceOffset = frame * sourceChannels;
                for (var channel = 0; channel < sourceChannels; channel++)
                {
                    sum += sourceBuffer[sourceOffset + channel];
                }

                buffer[offset + frame] = sum / sourceChannels;
            }

            return framesRead;
        }
        finally
        {
            ArrayPool<float>.Shared.Return(sourceBuffer);
        }
    }
}

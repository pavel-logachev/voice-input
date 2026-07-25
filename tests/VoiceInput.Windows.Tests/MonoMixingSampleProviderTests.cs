using NAudio.Wave;
using VoiceInput.Windows.Audio;

namespace VoiceInput.Windows.Tests.Audio;

public sealed class MonoMixingSampleProviderTests
{
    [Fact]
    public void ReadAveragesEveryInputChannelIntoMono()
    {
        var source = new ArraySampleProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2),
            [1f, 0f, 0f, 1f]);
        var provider = new MonoMixingSampleProvider(source);
        var output = new float[4];

        var read = provider.Read(output, 0, output.Length);

        Assert.Equal(2, read);
        Assert.Equal([0.5f, 0.5f], output[..read]);
        Assert.Equal(1, provider.WaveFormat.Channels);
        Assert.Equal(48_000, provider.WaveFormat.SampleRate);
    }

    private sealed class ArraySampleProvider(WaveFormat waveFormat, float[] samples) : ISampleProvider
    {
        private int offset;

        public WaveFormat WaveFormat { get; } = waveFormat;

        public int Read(float[] buffer, int bufferOffset, int count)
        {
            var available = Math.Min(count, samples.Length - offset);
            Array.Copy(samples, offset, buffer, bufferOffset, available);
            offset += available;
            return available;
        }
    }
}

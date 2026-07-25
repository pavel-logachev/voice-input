namespace VoiceInput.Core.Audio;

public sealed record RecordedAudio(float[] Samples, int SampleRate)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
}

public interface IAudioRecorder
{
    ValueTask StartAsync(CancellationToken cancellationToken);

    ValueTask<RecordedAudio> StopAsync(CancellationToken cancellationToken);

    ValueTask CancelAsync();
}

using VoiceInput.Core.Audio;

namespace VoiceInput.Windows.Audio;

public sealed class PcmFixtureAudioRecorder(string fixturePath) : IAudioRecorder
{
    private bool active;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (active)
        {
            throw new InvalidOperationException("Fixture recording is already active.");
        }

        active = true;
        return ValueTask.CompletedTask;
    }

    public async ValueTask<RecordedAudio> StopAsync(CancellationToken cancellationToken)
    {
        if (!active)
        {
            throw new InvalidOperationException("Fixture recording is not active.");
        }

        active = false;
        var bytes = await File.ReadAllBytesAsync(Path.GetFullPath(fixturePath), cancellationToken);
        if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0)
        {
            throw new InvalidDataException("VOICE_INPUT_PCM_FIXTURE must point to non-empty 16 kHz mono float32 PCM.");
        }

        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        return new RecordedAudio(samples, 16_000);
    }

    public ValueTask CancelAsync()
    {
        active = false;
        return ValueTask.CompletedTask;
    }
}

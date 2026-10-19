using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests;

public sealed class LocalAvailabilityTests
{
    [Fact]
    public async Task WorkerAvailabilityChecksFilesAndMissingWorkerFailsPredictably()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VoiceInput-availability-" + Guid.NewGuid().ToString("N"));
        var factory = new TranscriptionEngineFactory(new DpapiOpenAiApiKeyProvider(Path.Combine(directory, "key")), Path.Combine(directory, "worker.exe"));
        Assert.False(factory.IsLocalEngineAvailable);
        var exception = await Assert.ThrowsAsync<TranscriptionException>(() => factory.CreateAsync(
            new VoiceInput.Windows.Settings.AppSettings { Engine = VoiceInput.Windows.Settings.TranscriptionEngine.Local }, null, CancellationToken.None));
        Assert.Equal(TranscriptionErrorCode.WorkerExited, exception.Code);
    }

    [Fact]
    public void MissingModelHasActionableCodeWithoutDownloading()
    {
        var directory = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N"));
        var error = Assert.Throws<TranscriptionException>(() => new LocalAsrAssetProvisioner(directory).GetInstalledAssets());
        Assert.Equal(TranscriptionErrorCode.ModelNotDownloaded, error.Code);
        Assert.True(error.OpenSettings);
        Assert.False(Directory.Exists(directory));
    }
}

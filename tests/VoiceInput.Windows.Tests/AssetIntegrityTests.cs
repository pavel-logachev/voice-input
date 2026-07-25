using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests.Transcription;

public sealed class AssetIntegrityTests
{
    [Fact]
    public async Task VerifySha256AcceptsMatchingFileAndRejectsMismatch()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "voice-input");

            Assert.True(await AssetIntegrity.VerifySha256Async(
                path,
                "00371671d0febdcfd527b47d6bc64ad0c4c24b4c7358d1f81cf14b4eae82e533",
                CancellationToken.None));
            Assert.False(await AssetIntegrity.VerifySha256Async(
                path,
                new string('0', 64),
                CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

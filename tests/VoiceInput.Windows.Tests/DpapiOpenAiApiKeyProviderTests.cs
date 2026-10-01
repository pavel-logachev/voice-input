using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests.Transcription;

public sealed class DpapiOpenAiApiKeyProviderTests : IDisposable
{
    private const string ValidKey = "sk-proj-0123456789abcdefghijklmnopqrstuvwxyz0123";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "voiceinput-key-" + Guid.NewGuid().ToString("N"));

    private string CredentialPath => Path.Combine(directory, "secrets", "openai-api-key.dpapi");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SavedKeyRoundTripsAndIsNotStoredAsPlainText()
    {
        var provider = new DpapiOpenAiApiKeyProvider(CredentialPath);

        provider.Save($"  {ValidKey}  ");

        Assert.True(provider.HasKey);
        Assert.Equal(ValidKey, provider.GetApiKey());
        Assert.DoesNotContain(ValidKey, File.ReadAllText(CredentialPath, System.Text.Encoding.Latin1), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveReplacesAnExistingKey()
    {
        var provider = new DpapiOpenAiApiKeyProvider(CredentialPath);
        provider.Save(ValidKey);
        var replacement = ValidKey.Replace("0123", "9876", StringComparison.Ordinal);

        provider.Save(replacement);

        Assert.Equal(replacement, provider.GetApiKey());
        Assert.False(File.Exists(CredentialPath + ".tmp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-key")]
    [InlineData("sk-short")]
    [InlineData("pk-proj-0123456789abcdefghijklmnopqrstuvwxyz0123")]
    [InlineData("sk-proj-0123456789abcdefghijklmnop qrstuvwxyz0123")]
    public void ThingsThatAreNotApiKeysAreRejected(string candidate)
    {
        var provider = new DpapiOpenAiApiKeyProvider(CredentialPath);

        Assert.Throws<ArgumentException>(() => provider.Save(candidate));
        Assert.False(provider.HasKey);
    }

    [Fact]
    public void MissingKeyExplainsWhatToDo()
    {
        var provider = new DpapiOpenAiApiKeyProvider(CredentialPath);

        var error = Assert.Throws<InvalidOperationException>(provider.GetApiKey);

        Assert.Contains("не настроен", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DamagedFileIsReportedWithoutLeakingDetails()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CredentialPath)!);
        File.WriteAllBytes(CredentialPath, [1, 2, 3, 4, 5]);
        var provider = new DpapiOpenAiApiKeyProvider(CredentialPath);

        var error = Assert.Throws<InvalidOperationException>(provider.GetApiKey);

        Assert.Contains("Не удалось прочитать", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteRemovesTheKeyAndIsSafeWhenNothingIsStored()
    {
        var provider = new DpapiOpenAiApiKeyProvider(CredentialPath);
        provider.Delete();
        provider.Save(ValidKey);

        provider.Delete();

        Assert.False(provider.HasKey);
    }

    [Fact]
    public void LooksLikeApiKeyAcceptsRealisticKeysOnly()
    {
        Assert.True(DpapiOpenAiApiKeyProvider.LooksLikeApiKey(ValidKey));
        Assert.False(DpapiOpenAiApiKeyProvider.LooksLikeApiKey(null));
        Assert.False(DpapiOpenAiApiKeyProvider.LooksLikeApiKey("sk-" + new string('é', 50)));
    }
}

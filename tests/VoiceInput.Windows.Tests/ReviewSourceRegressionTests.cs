namespace VoiceInput.Windows.Tests;

/// <summary>Repository invariants from review: portable tests and one approved catalog for product copy.</summary>
public sealed class ReviewSourceRegressionTests
{
    [Fact]
    public void TestsDoNotDependOnADevelopersProfile()
    {
        foreach (var name in new[] { "LocalModelManagerTests.cs", "LocalModelLifetimeTests.cs", "CustomProviderTests.cs", "CrashLogTests.cs" })
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "tests", "VoiceInput.Windows.Tests", name));
            Assert.DoesNotContain("C:" + "\\\\Users", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@\"C:" + "\\Users", source, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Path.GetTempPath()", source, StringComparison.Ordinal);
            Assert.Contains("Directory.Delete", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TrayAndCredentialCopyLivesOnlyInCentralCatalog()
    {
        var root = RepositoryRoot();
        var status = File.ReadAllText(Path.Combine(root, "src", "VoiceInput.Windows", "Transcription", "EngineStatus.cs"));
        var keys = File.ReadAllText(Path.Combine(root, "src", "VoiceInput.Windows", "Transcription", "DpapiCustomApiKeyProvider.cs"));
        Assert.DoesNotContain("Готов ·", status, StringComparison.Ordinal);
        Assert.DoesNotContain("Не удалось прочитать", keys, StringComparison.Ordinal);
        Assert.DoesNotContain("недопустимые символы", keys, StringComparison.Ordinal);
        Assert.Equal("Готов · OpenAI", VoiceInput.Windows.Transcription.VoiceInputStrings.Get("tray.status.openAiReady"));
        Assert.Equal("Не удалось прочитать защищенный ключ своего сервера.", VoiceInput.Windows.Transcription.VoiceInputStrings.Get("custom.key.error.readFailed"));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VoiceInput.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Test source checkout not found");
    }
}

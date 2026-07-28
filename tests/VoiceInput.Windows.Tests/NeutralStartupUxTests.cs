namespace VoiceInput.Windows.Tests.Product;

public sealed class NeutralStartupUxTests
{
    [Fact]
    public void SuccessfulStartupDoesNotShowBalloonNotifications()
    {
        var source = ReadRepositoryFile("src", "VoiceInput.App", "App.xaml.cs");
        var initializationErrorHandler = source.IndexOf(
            "catch (Exception exception)",
            StringComparison.Ordinal);

        Assert.True(initializationErrorHandler > 0);
        Assert.DoesNotContain(
            "ShowBalloonTip",
            source[..initializationErrorHandler],
            StringComparison.Ordinal);
        var balloonCall = source.IndexOf("ShowBalloonTip", StringComparison.Ordinal);
        Assert.True(balloonCall >= 0);
        Assert.Equal(balloonCall, source.LastIndexOf("ShowBalloonTip", StringComparison.Ordinal));
        Assert.Contains("Voice Input — ошибка запуска", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UserFacingCopyIsHardwareNeutral()
    {
        var userFacingFiles = new[]
        {
            ReadRepositoryFile("README.md"),
            ReadRepositoryFile("docs", "ARCHITECTURE.md"),
            ReadRepositoryFile("installer", "README-RU.txt"),
            ReadRepositoryFile("src", "VoiceInput.App", "App.xaml.cs"),
            ReadRepositoryFile("src", "VoiceInput.App", "GlobalHotkeyRegistration.cs"),
        };

        foreach (var content in userFacingFiles)
        {
            Assert.DoesNotContain("Logitech", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Logi Options", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("голосовая клавиша", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Дождитесь уведомления", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("первый запуск может занять несколько минут", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TrayMenuExplainsUniversalShortcuts()
    {
        var source = ReadRepositoryFile("src", "VoiceInput.App", "App.xaml.cs");

        Assert.Contains("Горячие клавиши", source, StringComparison.Ordinal);
        Assert.Contains("Удерживать для записи — Ctrl + Shift + Space", source, StringComparison.Ordinal);
        Assert.Contains("Начать или завершить — Ctrl + Shift + K", source, StringComparison.Ordinal);
        Assert.Contains("Отменить диктовку — Esc", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot().FullName, .. parts]));

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "VoiceInput.sln")))
        {
            current = current.Parent;
        }

        return current ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

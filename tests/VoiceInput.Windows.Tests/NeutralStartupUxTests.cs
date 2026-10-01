namespace VoiceInput.Windows.Tests.Product;

public sealed class NeutralStartupUxTests
{
    [Fact]
    public void SuccessfulStartupDoesNotShowBalloonNotifications()
    {
        var source = ReadRepositoryFile("src", "VoiceInput.App", "App.xaml.cs");

        // Balloons are reserved for problems the user must act on: a failed engine start and a taken hotkey.
        var balloonCalls = AllIndexesOf(source, "ShowBalloonTip");
        Assert.Equal(2, balloonCalls.Count);

        var failureHandler = source.IndexOf("initialization-error", StringComparison.Ordinal);
        var conflictMethod = source.IndexOf("private void ShowHotkeyConflictWarning()", StringComparison.Ordinal);
        Assert.True(failureHandler > 0);
        Assert.True(conflictMethod > failureHandler);

        // One call sits in the initialization failure handler, the other in the hotkey conflict method.
        Assert.Single(balloonCalls, index => index > failureHandler && index < conflictMethod);
        Assert.Single(balloonCalls, index => index > conflictMethod);
        Assert.Contains("Voice Input — не удалось запустить распознавание", source, StringComparison.Ordinal);
        Assert.Contains("Voice Input — горячая клавиша занята", source, StringComparison.Ordinal);
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

    private static List<int> AllIndexesOf(string text, string value)
    {
        var indexes = new List<int>();
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            indexes.Add(index);
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return indexes;
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

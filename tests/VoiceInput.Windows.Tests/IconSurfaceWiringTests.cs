namespace VoiceInput.Windows.Tests.Appearance;

public sealed class IconSurfaceWiringTests
{
    [Fact]
    public void AllApplicationAndWindowsShellSurfacesUseQuietPulse()
    {
        var root = FindRepositoryRoot();
        var project = Read(root, "src", "VoiceInput.App", "VoiceInput.App.csproj");
        var window = Read(root, "src", "VoiceInput.App", "MainWindow.xaml");
        var application = Read(root, "src", "VoiceInput.App", "App.xaml.cs");
        var installer = Read(root, "installer", "VoiceInput.iss");

        Assert.Contains("<ApplicationIcon>..\\..\\assets\\VoiceInput.ico</ApplicationIcon>", project);
        Assert.Contains("<Resource Include=\"..\\..\\assets\\VoiceInput.ico\" Link=\"VoiceInput.ico\" />", project);
        Assert.Contains("Icon=\"pack://application:,,,/VoiceInput.ico\"", window);

        Assert.Contains("Icon.ExtractAssociatedIcon(processPath)", application);
        Assert.Contains("pack://application:,,,/VoiceInput.ico", application);
        Assert.DoesNotContain("SystemIcons.Application", application);

        Assert.Contains("SetupIconFile=..\\assets\\VoiceInput.ico", installer);
        Assert.Contains("UninstallDisplayIcon={app}\\VoiceInput.App.exe", installer);
        Assert.Equal(
            3,
            CountOccurrences(
                installer,
                "IconFilename: \"{app}\\VoiceInput.App.exe\"; IconIndex: 0"));
    }

    private static string Read(DirectoryInfo root, params string[] parts) =>
        File.ReadAllText(Path.Combine([root.FullName, .. parts]));

    private static int CountOccurrences(string value, string part)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(part, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += part.Length;
        }

        return count;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Voice Input repository root.");
    }
}

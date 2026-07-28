using System.Runtime.InteropServices;

namespace VoiceInput.Windows.Tests.Appearance;

public sealed class IconAssetTests
{
    [Fact]
    public void ApplicationIconContainsAllWindowsShellSizes()
    {
        var root = FindRepositoryRoot();
        var iconPath = Path.Combine(root.FullName, "assets", "VoiceInput.ico");

        using var stream = File.OpenRead(iconPath);
        using var reader = new BinaryReader(stream);
        Assert.Equal((ushort)0, reader.ReadUInt16());
        Assert.Equal((ushort)1, reader.ReadUInt16());
        var imageCount = reader.ReadUInt16();
        var sizes = new HashSet<int>();

        for (var index = 0; index < imageCount; index++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            sizes.Add(width == 0 ? 256 : width);
            Assert.Equal(width, height);
            _ = reader.ReadBytes(14);
        }

        Assert.Equal([16, 20, 24, 32, 48, 64, 128, 256], sizes.Order());
    }

    [Fact]
    public void BuiltApplicationExposesIconToWindowsShell()
    {
        var root = FindRepositoryRoot();
        var executablePath = Path.Combine(
            root.FullName,
            "src",
            "VoiceInput.App",
            "bin",
            "Release",
            "net10.0-windows",
            "VoiceInput.App.exe");

        Assert.True(File.Exists(executablePath), $"Application executable not found: {executablePath}");
        Assert.True(ExtractIconEx(executablePath, -1, null, null, 0) > 0);
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint ExtractIconEx(
        string fileName,
        int iconIndex,
        nint[]? largeIcons,
        nint[]? smallIcons,
        uint iconCount);
}

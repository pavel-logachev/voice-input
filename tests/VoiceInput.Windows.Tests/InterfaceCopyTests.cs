namespace VoiceInput.Windows.Tests.Product;

public sealed class InterfaceCopyTests
{
    private static readonly string[] TextExtensions = [".cs", ".xaml", ".md", ".txt", ".iss", ".html", ".py", ".yml"];
    private static readonly string[] TextFolders = ["src", "installer", "docs", "tools", "scripts", ".github"];
    private static readonly string[] FocusableControls = ["Button", "PasswordBox", "ComboBox", "ComboBoxItem", "CheckBox", "RadioButton"];

    [Fact]
    public void TextsAreWrittenWithoutTheLetterYo()
    {
        // The project's rule for every text a user or a reader sees: the Russian letter yo is written as ye.
        var root = FindRepositoryRoot();
        var offenders = TextFolders
            .Select(folder => Path.Combine(root.FullName, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(root.FullName, "*.md"))
            .Where(path => TextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).AsSpan().IndexOfAny('\u0451', '\u0401') >= 0)
            .Select(path => Path.GetRelativePath(root.FullName, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SettingsWindowKeepsKeyboardBasics()
    {
        var markup = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, "src", "VoiceInput.App", "SettingsWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, "src", "VoiceInput.App", "SettingsWindow.xaml.cs"));

        // Esc closes the window; Enter in the key field saves the key instead of closing the window.
        Assert.Contains("IsCancel=\"True\"", markup, StringComparison.Ordinal);
        Assert.Contains("KeyBox.PreviewKeyDown += OnKeyBoxKeyDown;", code, StringComparison.Ordinal);

        // No control falls back to the system focus rectangle, which is invisible on a dark window.
        // A style names its control in its opening tag; a style based on another one inherits the focus ring.
        var styles = markup.Split("<Style ", StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Where(style => FocusableControls.Any(type => OpeningTag(style).Contains($"TargetType=\"{type}\"", StringComparison.Ordinal)))
            .Where(style => !OpeningTag(style).Contains("BasedOn=", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(7, styles.Length);
        Assert.All(styles, style => Assert.Contains("<Setter Property=\"FocusVisualStyle\"", style, StringComparison.Ordinal));
    }

    private static string OpeningTag(string style) => style[..style.IndexOf('>', StringComparison.Ordinal)];

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

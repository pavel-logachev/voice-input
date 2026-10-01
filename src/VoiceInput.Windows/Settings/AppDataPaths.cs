namespace VoiceInput.Windows.Settings;

/// <summary>
/// Where Voice Input keeps its settings and secrets. VOICE_INPUT_DATA_DIR redirects everything,
/// which lets tests and side-by-side runs avoid touching the real profile.
/// </summary>
public static class AppDataPaths
{
    public const string DataDirectoryVariable = "VOICE_INPUT_DATA_DIR";

    public static string Root
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(DataDirectoryVariable);
            return string.IsNullOrWhiteSpace(overridden)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceInput")
                : Path.GetFullPath(overridden);
        }
    }
}

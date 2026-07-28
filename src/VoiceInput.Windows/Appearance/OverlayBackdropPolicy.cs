namespace VoiceInput.Windows.Appearance;

public enum OverlayBackdropMode
{
    TintOnly,
    Acrylic,
}

public static class OverlayBackdropPolicy
{
    private const int MinimumAcrylicBuild = 22000;

    public static OverlayBackdropMode Select(
        Version windowsVersion,
        bool compositionEnabled,
        bool highContrast,
        bool transparencyEnabled)
    {
        ArgumentNullException.ThrowIfNull(windowsVersion);

        var supportedWindows = windowsVersion.Major > 10 ||
            windowsVersion.Major == 10 && windowsVersion.Build >= MinimumAcrylicBuild;
        return supportedWindows && compositionEnabled && !highContrast && transparencyEnabled
            ? OverlayBackdropMode.Acrylic
            : OverlayBackdropMode.TintOnly;
    }
}

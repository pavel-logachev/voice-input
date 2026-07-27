using VoiceInput.Windows.Appearance;

namespace VoiceInput.Windows.Tests.Appearance;

public sealed class OverlayBackdropPolicyTests
{
    [Theory]
    [InlineData(17763)]
    [InlineData(19045)]
    [InlineData(22621)]
    [InlineData(26200)]
    public void SupportedWindowsUsesAcrylicWhenDesktopEffectsAreAvailable(int build)
    {
        var mode = OverlayBackdropPolicy.Select(
            new Version(10, 0, build),
            compositionEnabled: true,
            highContrast: false,
            transparencyEnabled: true);

        Assert.Equal(OverlayBackdropMode.Acrylic, mode);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void AccessibilityOrCompositionRestrictionsUseTintOnly(
        bool compositionEnabled,
        bool highContrast,
        bool transparencyEnabled)
    {
        var mode = OverlayBackdropPolicy.Select(
            new Version(10, 0, 22621),
            compositionEnabled,
            highContrast,
            transparencyEnabled);

        Assert.Equal(OverlayBackdropMode.TintOnly, mode);
    }

    [Theory]
    [InlineData(6, 3, 9600)]
    [InlineData(10, 0, 17134)]
    [InlineData(10, 0, 17762)]
    public void UnsupportedWindowsUsesTintOnly(int major, int minor, int build)
    {
        var mode = OverlayBackdropPolicy.Select(
            new Version(major, minor, build),
            compositionEnabled: true,
            highContrast: false,
            transparencyEnabled: true);

        Assert.Equal(OverlayBackdropMode.TintOnly, mode);
    }
}

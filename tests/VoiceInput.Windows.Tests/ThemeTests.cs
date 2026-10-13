using System.Windows.Media;
using VoiceInput.App;

namespace VoiceInput.Windows.Tests.Appearance;

public sealed class ThemeTests
{
    private static readonly UiPalette Palette = Theme.Graphite;

    public static TheoryData<string, Color, Color> TextOnSurfaces => new()
    {
        { "text on the window", Palette.Text, Palette.Window },
        { "text on a card", Palette.Text, Palette.Card },
        { "hints on the window", Palette.TextMuted, Palette.Window },
        { "hints on a card", Palette.TextMuted, Palette.Card },
        { "placeholder in a field", Palette.TextFaint, Palette.Field },
        { "inactive button label", Palette.TextFaint, Palette.Card },
        { "main button label", Palette.OnAccent, Palette.Accent },
        { "main button label under the pointer", Palette.OnAccent, Palette.AccentHover },
        { "list row under the pointer", Palette.OnHighlight, Palette.Highlight },
        { "key saved", Palette.Success, Palette.Card },
        { "key missing", Palette.Warning, Palette.Card },
        { "key rejected", Palette.Error, Palette.Card },
        { "overlay title", Palette.OverlayText, Opaque(Palette.OverlaySurface) },
        { "overlay detail", Palette.OverlayMuted, Opaque(Palette.OverlaySurface) },
        { "tray menu item", Palette.Text, Palette.MenuSurface },
        { "tray menu shortcut", Palette.TextMuted, Palette.MenuSurface },
        { "tray menu item under the pointer", Palette.Text, Palette.MenuSelection },
    };

    public static TheoryData<string, Color> StageMarks => new()
    {
        { "recording", Palette.Record },
        { "recognising", Palette.Process },
        { "typing", Palette.Insert },
        { "done", Palette.Success },
        { "nothing heard", Palette.Warning },
        { "failed", Palette.Error },
        { "note", Palette.Neutral },
    };

    [Theory]
    [MemberData(nameof(TextOnSurfaces))]
    public void TextIsReadableOnItsSurface(string what, Color text, Color surface)
    {
        // WCAG AA for body text.
        Assert.True(Theme.Contrast(text, surface) >= 4.5, $"{what}: {Theme.Contrast(text, surface):0.00}:1");
    }

    [Theory]
    [MemberData(nameof(StageMarks))]
    public void StageMarkStandsOutOnTheOverlay(string stage, Color mark)
    {
        // WCAG AA for graphics; the dark sign inside a filled mark has to read as well.
        var surface = Opaque(Palette.OverlaySurface);
        Assert.True(Theme.Contrast(mark, surface) >= 3, $"{stage}: {Theme.Contrast(mark, surface):0.00}:1");
        Assert.True(Theme.Contrast(Palette.OnAccent, mark) >= 4.5, $"{stage}, sign: {Theme.Contrast(Palette.OnAccent, mark):0.00}:1");
    }

    [Fact]
    public void WaveEndsInTheBrightestColour()
    {
        // The last colour is the cursor the wave turns into: it must stay the brightest point, as on the icon.
        var surface = Opaque(Palette.OverlaySurface);
        var cursor = Theme.Contrast(Palette.Wave[^1], surface);
        Assert.All(Palette.Wave.Take(Palette.Wave.Count - 1), colour => Assert.True(Theme.Contrast(colour, surface) < cursor));
        Assert.All(Palette.Wave, colour => Assert.True(Theme.Contrast(colour, surface) >= 3));
    }

    [Fact]
    public void ContrastFollowsTheWcagDefinition()
    {
        Assert.Equal(21, Theme.Contrast(Colors.Black, Colors.White), precision: 2);
        Assert.Equal(1, Theme.Contrast(Colors.Gray, Colors.Gray), precision: 2);
    }

    private static Color Opaque(Color colour) => Color.FromRgb(colour.R, colour.G, colour.B);
}

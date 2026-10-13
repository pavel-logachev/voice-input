using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SystemColors = System.Windows.SystemColors;

namespace VoiceInput.App;

/// <summary>Every colour the overlay, the settings window and the tray menu use. One palette, three surfaces.</summary>
internal sealed record UiPalette
{
    public bool HighContrast { get; init; }

    // Surfaces of the settings window.
    public required Color Window { get; init; }

    public required Color Card { get; init; }

    public required Color CardBorder { get; init; }

    public required Color Divider { get; init; }

    public required Color Field { get; init; }

    public required Color FieldBorder { get; init; }

    public required Color FieldBorderHover { get; init; }

    public required Color Subtle { get; init; }

    public required Color SubtleHover { get; init; }

    public required Color SubtlePressed { get; init; }

    /// <summary>The row under the pointer in a list, and the chosen segment.</summary>
    public required Color Highlight { get; init; }

    public required Color OnHighlight { get; init; }

    // Text.
    public required Color Text { get; init; }

    public required Color TextMuted { get; init; }

    public required Color TextFaint { get; init; }

    // The one accent: the main button, switches, selection marks.
    public required Color Accent { get; init; }

    public required Color AccentHover { get; init; }

    public required Color AccentPressed { get; init; }

    public required Color OnAccent { get; init; }

    public required Color Focus { get; init; }

    // Overlay.
    public required Color OverlaySurface { get; init; }

    public required Color OverlayBorder { get; init; }

    public required Color OverlayText { get; init; }

    public required Color OverlayMuted { get; init; }

    public required Color OverlayTrack { get; init; }

    public required Color KeyCap { get; init; }

    public required Color KeyCapBorder { get; init; }

    public required Color KeyCapText { get; init; }

    // Stages of a dictation and its outcomes.
    public required Color Record { get; init; }

    public required Color Process { get; init; }

    public required Color Insert { get; init; }

    public required Color Success { get; init; }

    public required Color Warning { get; init; }

    public required Color Error { get; init; }

    public required Color Neutral { get; init; }

    /// <summary>Colours of the icon wave, oldest sound first; the last one is the cursor.</summary>
    public required IReadOnlyList<Color> Wave { get; init; }

    // Tray menu.
    public required Color MenuSurface { get; init; }

    public required Color MenuBorder { get; init; }

    public required Color MenuSelection { get; init; }
}

internal static class Theme
{
    /// <summary>
    /// Neutral graphite, close to Windows 11 itself: colour appears only where it means something —
    /// the stage of a dictation, the one accent, and the wave of the application icon.
    /// </summary>
    internal static readonly UiPalette Graphite = new()
    {
        Window = Hex("#1C1C1F"),
        Card = Hex("#27272B"),
        CardBorder = Hex("#34343A"),
        Divider = Hex("#34343A"),
        Field = Hex("#1A1A1D"),
        FieldBorder = Hex("#45454D"),
        FieldBorderHover = Hex("#5E5E69"),
        Subtle = Hex("#35353B"),
        SubtleHover = Hex("#404047"),
        SubtlePressed = Hex("#2E2E33"),
        Highlight = Hex("#404047"),
        OnHighlight = Hex("#F5F5F7"),
        Text = Hex("#F5F5F7"),
        TextMuted = Hex("#B1B1BB"),
        TextFaint = Hex("#9494A0"),
        Accent = Hex("#A99BFF"),
        AccentHover = Hex("#BBB0FF"),
        AccentPressed = Hex("#978AEA"),
        OnAccent = Hex("#131318"),
        Focus = Hex("#F5F5F7"),
        OverlaySurface = Hex("#F51C1C1F"),
        OverlayBorder = Hex("#2EFFFFFF"),
        OverlayText = Hex("#F5F5F7"),
        OverlayMuted = Hex("#B1B1BB"),
        OverlayTrack = Hex("#4A4A52"),
        KeyCap = Hex("#12FFFFFF"),
        KeyCapBorder = Hex("#33FFFFFF"),
        KeyCapText = Hex("#C8C8D0"),
        Record = Hex("#FF6F61"),
        Process = Hex("#A99BFF"),
        Insert = Hex("#8AB8FF"),
        Success = Hex("#62D4A0"),
        Warning = Hex("#F2C66D"),
        Error = Hex("#FF7A8E"),
        Neutral = Hex("#B1B1BB"),
        Wave = [Hex("#A99BFF"), Hex("#D698D8"), Hex("#FF8FB1"), Hex("#FFB08A"), Hex("#F8F7FF")],
        MenuSurface = Hex("#232326"),
        MenuBorder = Hex("#3A3A41"),
        MenuSelection = Hex("#36363C"),
    };

    /// <summary>The palette in use. In a Windows high-contrast theme the system colours replace it.</summary>
    public static UiPalette Current => UiEnvironment.HighContrast ? HighContrast() : Graphite;

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Publishes the palette as brushes the XAML refers to by name: Brush.Text, Brush.Accent and so on.</summary>
    public static void Apply(FrameworkElement root)
    {
        var palette = Current;
        foreach (var property in typeof(UiPalette).GetProperties())
        {
            if (property.PropertyType == typeof(Color))
            {
                root.Resources["Brush." + property.Name] = Brush((Color)property.GetValue(palette)!);
            }
        }
    }

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    public static System.Drawing.Color ToDrawing(Color color) =>
        System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>Contrast ratio of two opaque colours as WCAG defines it: from 1 (the same) to 21 (black on white).</summary>
    public static double Contrast(Color first, Color second)
    {
        var lighter = Math.Max(Luminance(first), Luminance(second));
        var darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    // Only the colours the user chose in Windows: window, text, highlight. Shape and outline carry the rest.
    private static UiPalette HighContrast()
    {
        var window = SystemColors.WindowColor;
        var text = SystemColors.WindowTextColor;
        var highlight = SystemColors.HighlightColor;
        var onHighlight = SystemColors.HighlightTextColor;
        var gray = SystemColors.GrayTextColor;
        return new UiPalette
        {
            HighContrast = true,
            Window = window,
            Card = window,
            CardBorder = text,
            Divider = text,
            Field = window,
            FieldBorder = text,
            FieldBorderHover = highlight,
            Subtle = window,
            SubtleHover = window,
            SubtlePressed = window,
            Highlight = highlight,
            OnHighlight = onHighlight,
            Text = text,
            TextMuted = text,
            TextFaint = gray,
            Accent = highlight,
            AccentHover = highlight,
            AccentPressed = highlight,
            OnAccent = onHighlight,
            Focus = text,
            OverlaySurface = window,
            OverlayBorder = text,
            OverlayText = text,
            OverlayMuted = text,
            OverlayTrack = gray,
            KeyCap = window,
            KeyCapBorder = text,
            KeyCapText = text,
            Record = highlight,
            Process = highlight,
            Insert = highlight,
            Success = highlight,
            Warning = highlight,
            Error = highlight,
            Neutral = text,
            Wave = [highlight],
            MenuSurface = window,
            MenuBorder = text,
            MenuSelection = highlight,
        };
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255.0;
            return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value);
}

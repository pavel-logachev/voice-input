using System.Windows;
using System.Windows.Media;

namespace VoiceInput.App;

/// <summary>
/// The display and accessibility settings the windows adapt to. The overrides exist for the UI preview tool,
/// which has to draw other scales and the high-contrast look without changing Windows settings.
/// </summary>
internal static class UiEnvironment
{
    internal static bool? HighContrastOverride { get; set; }

    internal static bool? AnimationsOverride { get; set; }

    internal static double? ScaleOverride { get; set; }

    public static bool HighContrast => HighContrastOverride ?? SystemParameters.HighContrast;

    public static bool Animations => AnimationsOverride ?? (SystemParameters.ClientAreaAnimation && !HighContrast);

    public static double ScaleOf(Visual visual) => ScaleOverride ?? VisualTreeHelper.GetDpi(visual).DpiScaleX;

    /// <summary>
    /// At 100 % small text is sharper when it is fitted to the pixel grid; at larger scales the true letter shapes look better.
    /// </summary>
    public static void ApplyTextFormatting(Visual root)
    {
        TextOptions.SetTextFormattingMode(
            root,
            ScaleOf(root) > 1.01 ? TextFormattingMode.Ideal : TextFormattingMode.Display);
    }
}

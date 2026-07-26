namespace VoiceInput.Core.Activation;

public readonly record struct OverlayWorkArea(double Left, double Top, double Width, double Height);

public readonly record struct OverlayPlacement(double Left, double Top, double Width, double Height);

public static class OverlayPositioning
{
    public static OverlayPlacement BottomCenter(
        OverlayWorkArea workArea,
        double actualWidth,
        double actualHeight,
        double requestedWidth,
        double requestedHeight,
        double margin)
    {
        var width = ResolveExtent(actualWidth, requestedWidth);
        var height = ResolveExtent(actualHeight, requestedHeight);
        var safeMargin = Math.Max(0, margin);

        var maximumLeft = Math.Max(workArea.Left, workArea.Left + workArea.Width - width);
        var centeredLeft = workArea.Left + ((workArea.Width - width) / 2);
        var left = Math.Clamp(centeredLeft, workArea.Left, maximumLeft);

        var maximumTop = Math.Max(workArea.Top, workArea.Top + workArea.Height - height);
        var desiredTop = workArea.Top + workArea.Height - height - safeMargin;
        var top = Math.Clamp(desiredTop, workArea.Top, maximumTop);

        return new OverlayPlacement(left, top, width, height);
    }

    private static double ResolveExtent(double actual, double requested) =>
        double.IsFinite(actual) && actual > 0
            ? Math.Max(actual, requested)
            : requested;
}

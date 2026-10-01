using System.Windows;
using System.Windows.Media;
using VoiceInput.Core.Audio;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;

namespace VoiceInput.App;

/// <summary>A row of bars showing the recent microphone level, drawn over a thin baseline.</summary>
public sealed class RecordingLevelMeter : FrameworkElement
{
    private const int BarCount = 22;
    private const double DesiredWidth = 212;
    private const double DesiredHeight = 14;

    private readonly RecordingLevelHistory history = new(BarCount);
    private SolidColorBrush inactiveBrush = CreateBrush(Color.FromRgb(68, 72, 84));
    private SolidColorBrush activityBrush = CreateBrush(Color.FromRgb(255, 117, 109));

    public RecordingLevelMeter()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    public void SetLevel(float level)
    {
        history.Push(level);
        InvalidateVisual();
    }

    public void SetPalette(Color activity, Color inactive)
    {
        activityBrush = CreateBrush(activity);
        inactiveBrush = CreateBrush(inactive);
        InvalidateVisual();
    }

    public void Reset()
    {
        history.Reset();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(DesiredWidth, DesiredHeight);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var centerY = ActualHeight / 2;
        var levels = history.Values;
        var cellWidth = ActualWidth / BarCount;
        var barWidth = Math.Min(3, cellWidth * 0.42);
        for (var index = 0; index < levels.Length; index++)
        {
            var left = (index * cellWidth) + ((cellWidth - barWidth) / 2);

            // Every slot keeps a thin idle tick, so the meter reads as a ruler even in silence.
            drawingContext.DrawRoundedRectangle(
                inactiveBrush,
                null,
                new Rect(left, centerY - 1, barWidth, 2),
                1,
                1);

            var level = levels[index];
            if (level <= 0)
            {
                continue;
            }

            // Older bars (on the left) fade, so the meter reads as sound travelling away.
            var age = levels.Length <= 1 ? 1.0 : (double)index / (levels.Length - 1);
            drawingContext.PushOpacity(0.32 + (0.68 * age));
            var shapedLevel = Math.Sqrt(level);
            var height = 2 + (shapedLevel * Math.Max(0, ActualHeight - 2));
            drawingContext.DrawRoundedRectangle(
                activityBrush,
                null,
                new Rect(left, centerY - (height / 2), barWidth, height),
                1.5,
                1.5);
            drawingContext.Pop();
        }
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

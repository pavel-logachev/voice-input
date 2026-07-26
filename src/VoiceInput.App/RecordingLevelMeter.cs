using System.Windows;
using System.Windows.Media;
using VoiceInput.Core.Audio;

namespace VoiceInput.App;

public sealed class RecordingLevelMeter : FrameworkElement
{
    private const int BarCount = 20;
    private const double DesiredWidth = 140;
    private const double DesiredHeight = 12;

    private readonly RecordingLevelHistory history = new(BarCount);
    private readonly System.Windows.Media.Pen baselinePen;
    private readonly SolidColorBrush activityBrush;

    public RecordingLevelMeter()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;

        var baselineBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105));
        baselineBrush.Freeze();
        baselinePen = new System.Windows.Media.Pen(baselineBrush, 1.5);
        baselinePen.Freeze();

        activityBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 211, 238));
        activityBrush.Freeze();
    }

    public void SetLevel(float level)
    {
        history.Push(level);
        InvalidateVisual();
    }

    public void Reset()
    {
        history.Reset();
        InvalidateVisual();
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) =>
        new(DesiredWidth, DesiredHeight);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var centerY = ActualHeight / 2;
        drawingContext.DrawLine(
            baselinePen,
            new System.Windows.Point(0, centerY),
            new System.Windows.Point(ActualWidth, centerY));

        var levels = history.Values;
        var cellWidth = ActualWidth / BarCount;
        var barWidth = Math.Min(3, cellWidth * 0.45);
        for (var index = 0; index < levels.Length; index++)
        {
            var level = levels[index];
            if (level <= 0)
            {
                continue;
            }

            var shapedLevel = Math.Sqrt(level);
            var height = 2 + (shapedLevel * Math.Max(0, ActualHeight - 2));
            var left = (index * cellWidth) + ((cellWidth - barWidth) / 2);
            var rectangle = new Rect(left, centerY - (height / 2), barWidth, height);
            drawingContext.DrawRoundedRectangle(activityBrush, null, rectangle, 1.5, 1.5);
        }
    }
}

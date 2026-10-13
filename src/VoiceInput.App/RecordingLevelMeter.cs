using System.Windows;
using System.Windows.Media;
using VoiceInput.Core.Audio;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;

namespace VoiceInput.App;

/// <summary>
/// The recent microphone level drawn as the wave from the application icon: rounded bars running
/// from violet to amber into a still white cursor. The newest sound is on the right.
/// </summary>
public sealed class RecordingLevelMeter : FrameworkElement
{
    private const int Capacity = 48;
    private const double BarWidth = 4;
    private const double BarGap = 3;
    private const double IdleHeight = 4;
    private const double CursorGap = 6;

    private readonly RecordingLevelHistory history = new(Capacity);
    private IReadOnlyList<Color> wave = [Colors.White];
    private SolidColorBrush idleBrush = Theme.Brush(Colors.Gray);
    private SolidColorBrush cursorBrush = Theme.Brush(Colors.White);
    private SolidColorBrush[] barBrushes = [];

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

    /// <param name="colours">The wave, oldest sound first; the last colour is the cursor. One colour draws plain bars.</param>
    /// <param name="idle">The mark of a silent slot when the wave is a single colour.</param>
    public void SetAppearance(IReadOnlyList<Color> colours, Color idle)
    {
        ArgumentNullException.ThrowIfNull(colours);
        ArgumentOutOfRangeException.ThrowIfZero(colours.Count);
        wave = colours;
        idleBrush = Theme.Brush(idle);
        cursorBrush = Theme.Brush(colours[^1]);
        barBrushes = [];
        InvalidateVisual();
    }

    public void Reset()
    {
        history.Reset();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(
            double.IsInfinity(availableSize.Width) ? 96 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 18 : availableSize.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var gradient = wave.Count > 2;
        var barsWidth = gradient ? ActualWidth - BarWidth - CursorGap : ActualWidth;
        RenderBars(drawingContext, barsWidth, gradient);
        if (!gradient)
        {
            return;
        }

        // The cursor the wave turns into: it does not move with the sound.
        var cursorHeight = Math.Round(ActualHeight * 0.9);
        drawingContext.DrawRoundedRectangle(
            cursorBrush,
            null,
            new Rect(ActualWidth - BarWidth, (ActualHeight - cursorHeight) / 2, BarWidth, cursorHeight),
            BarWidth / 2,
            BarWidth / 2);
    }

    private void RenderBars(DrawingContext drawingContext, double width, bool gradient)
    {
        var levels = history.Values;
        var count = Math.Clamp((int)Math.Floor((width + BarGap) / (BarWidth + BarGap)), 1, levels.Length);
        // Spread the bars over the whole width, so the last one touches the right edge.
        var step = count > 1 ? (width - BarWidth) / (count - 1) : 0;
        var centerY = ActualHeight / 2;
        var radius = BarWidth / 2;
        if (barBrushes.Length != count)
        {
            // The meter redraws on every audio block: the colours of the bars are worked out once per width.
            barBrushes = new SolidColorBrush[count];
            for (var index = 0; index < count; index++)
            {
                barBrushes[index] = Theme.Brush(gradient ? WaveColour(Position(index, count)) : wave[0]);
            }
        }

        for (var index = 0; index < count; index++)
        {
            var position = Position(index, count);
            var left = index * step;
            var level = levels[levels.Length - count + index];
            var colour = barBrushes[index];

            // Every slot keeps a small idle mark, so the meter reads as a wave even in silence.
            if (level <= 0)
            {
                drawingContext.PushOpacity(gradient ? 0.38 : 1);
                drawingContext.DrawRoundedRectangle(
                    gradient ? colour : idleBrush,
                    null,
                    new Rect(left, centerY - (IdleHeight / 2), BarWidth, IdleHeight),
                    radius,
                    radius);
                drawingContext.Pop();
                continue;
            }

            // Older bars (on the left) fade, so the meter reads as sound travelling away.
            drawingContext.PushOpacity(0.55 + (0.45 * position));
            var height = IdleHeight + (Math.Sqrt(level) * Math.Max(0, ActualHeight - IdleHeight));
            drawingContext.DrawRoundedRectangle(
                colour,
                null,
                new Rect(left, centerY - (height / 2), BarWidth, height),
                radius,
                radius);
            drawingContext.Pop();
        }
    }

    private static double Position(int index, int count) => count <= 1 ? 1.0 : (double)index / (count - 1);

    private Color WaveColour(double position)
    {
        // The last colour is the cursor; the bars use the ones before it.
        var stops = wave.Count - 1;
        var scaled = Math.Clamp(position, 0, 1) * (stops - 1);
        var index = Math.Min((int)scaled, stops - 2);
        var mix = scaled - index;
        var from = wave[index];
        var to = wave[index + 1];
        return Color.FromRgb(
            (byte)(from.R + ((to.R - from.R) * mix)),
            (byte)(from.G + ((to.G - from.G) * mix)),
            (byte)(from.B + ((to.B - from.B) * mix)));
    }
}

using VoiceInput.Core.Audio;

namespace VoiceInput.Core.Tests.Audio;

public sealed class RecordingLevelHistoryTests
{
    [Fact]
    public void EnvelopeDependsOnElapsedTimeNotFrameCount()
    {
        var fast = new RecordingLevelHistory(48);
        var slow = new RecordingLevelHistory(48);
        for (var i = 0; i < 12; i++) fast.Advance(1, TimeSpan.FromMilliseconds(10));
        for (var i = 0; i < 3; i++) slow.Advance(1, TimeSpan.FromMilliseconds(40));
        Assert.InRange(fast.CurrentLevel, 0.98f, 1f);
        Assert.Equal(fast.CurrentLevel, slow.CurrentLevel, 5);
        for (var i = 0; i < 24; i++) fast.Advance(0, TimeSpan.FromMilliseconds(10));
        for (var i = 0; i < 6; i++) slow.Advance(0, TimeSpan.FromMilliseconds(40));
        Assert.Equal(fast.CurrentLevel, slow.CurrentLevel, 5);
        Assert.InRange(fast.CurrentLevel, 0.03f, 0.07f);
    }

    [Fact]
    public void ScrollMovesContinuouslyBetweenSamples()
    {
        var history = new RecordingLevelHistory(4);
        history.Advance(1, TimeSpan.FromMilliseconds(15));
        Assert.Equal(0.5, history.ScrollFraction, 5);
        history.Advance(1, TimeSpan.FromMilliseconds(15));
        Assert.Equal(0, history.ScrollFraction, 5);
        Assert.True(history.Values[^1] > 0);
    }

    [Fact]
    public void HistoryStartsAsAFlatLine()
    {
        var history = new RecordingLevelHistory(8);

        Assert.All(history.Values.ToArray(), level => Assert.Equal(0, level));
    }

    [Fact]
    public void PushAddsAClampedLevelAtTheLeadingEdge()
    {
        var history = new RecordingLevelHistory(4);

        history.Push(2f);

        Assert.Equal([0f, 0f, 0f], history.Values[..3].ToArray());
        Assert.InRange(history.Values[^1], 0.69f, 0.71f);
    }

    [Fact]
    public void SilenceDecaysToAFlatLine()
    {
        var history = new RecordingLevelHistory(4);
        history.Push(1f);

        for (var index = 0; index < 24; index++)
        {
            history.Push(0);
        }

        Assert.All(history.Values.ToArray(), level => Assert.Equal(0, level));
    }

    [Fact]
    public void ResetImmediatelyClearsVisibleActivity()
    {
        var history = new RecordingLevelHistory(4);
        history.Push(0.8f);

        history.Reset();

        Assert.All(history.Values.ToArray(), level => Assert.Equal(0, level));
    }
}

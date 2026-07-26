using VoiceInput.Core.Audio;

namespace VoiceInput.Core.Tests.Audio;

public sealed class RecordingLevelHistoryTests
{
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

        Assert.Equal([0f, 0f, 0f, 1f], history.Values.ToArray());
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

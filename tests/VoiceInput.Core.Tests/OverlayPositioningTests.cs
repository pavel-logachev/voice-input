using VoiceInput.Core.Activation;

namespace VoiceInput.Core.Tests.Activation;

public sealed class OverlayPositioningTests
{
    [Fact]
    public void FirstShowUsesRequestedHeightBeforeActualHeightExists()
    {
        var placement = OverlayPositioning.BottomCenter(
            new OverlayWorkArea(0, 0, 2_560, 1_392),
            actualWidth: 0,
            actualHeight: 0,
            requestedWidth: 380,
            requestedHeight: 160,
            margin: 24);

        Assert.Equal(1_090, placement.Left);
        Assert.Equal(1_208, placement.Top);
        Assert.Equal(380, placement.Width);
        Assert.Equal(160, placement.Height);
        Assert.True(placement.Top + placement.Height <= 1_392);
    }
}

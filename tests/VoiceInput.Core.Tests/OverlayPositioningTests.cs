using VoiceInput.Core.Activation;

namespace VoiceInput.Core.Tests.Activation;

public sealed class OverlayPositioningTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void FirstShowUsesMeasuredCapsuleAndShadowMarginInPhysicalPixels(double scale)
    {
        // ActualHeight is zero before Show; content measure is 50 + 28 + 28 DIP.
        var placement = OverlayPositioning.BottomCenter(
            new OverlayWorkArea(-1920, -200, 1920, 1040),
            0, 0, 416 * scale, 106 * scale, 0);
        Assert.Equal(840, placement.Top + placement.Height, 6);
        Assert.True(placement.Top + (78 * scale) < 840); // capsule, excluding bottom shadow margin
        Assert.Equal(-960, placement.Left + placement.Width / 2, 6);
    }

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

using VoiceInput.Windows.Appearance;

namespace VoiceInput.Windows.Tests.Appearance;

public sealed class RoundedWindowRegionTests
{
    [Theory]
    [InlineData(96, 24)]
    [InlineData(120, 30)]
    [InlineData(144, 36)]
    [InlineData(192, 48)]
    public void GeometryScalesTwelvePixelCornerRadiusWithDpi(uint dpi, int expectedDiameter)
    {
        var geometry = RoundedWindowRegion.CalculateGeometry(
            clientWidth: 300,
            clientHeight: 96,
            dpi,
            logicalCornerRadius: 12);

        Assert.Equal(301, geometry.Right);
        Assert.Equal(97, geometry.Bottom);
        Assert.Equal(expectedDiameter, geometry.EllipseWidth);
        Assert.Equal(expectedDiameter, geometry.EllipseHeight);
    }

    [Theory]
    [InlineData(0, 96, 96, 12)]
    [InlineData(300, 0, 96, 12)]
    [InlineData(300, 96, 0, 12)]
    [InlineData(300, 96, 96, 0)]
    public void GeometryRejectsInvalidDimensions(
        int clientWidth,
        int clientHeight,
        uint dpi,
        double logicalCornerRadius)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoundedWindowRegion.CalculateGeometry(
            clientWidth,
            clientHeight,
            dpi,
            logicalCornerRadius));
    }
}

using VoiceInput.Core.Activation;

namespace VoiceInput.Core.Tests.Activation;

public sealed class CompactOverlayPresentationTests
{
    [Fact]
    public void ListeningUsesSingleShortLabelAndMeter()
    {
        var presentation = CompactOverlayPresentation.For(ActivationVisualState.Listening);

        Assert.Equal("Слушаю", presentation.Title);
        Assert.Equal(CompactOverlayActivity.Meter, presentation.Activity);
    }

    [Fact]
    public void ProcessingUsesGenericLabelAndProgressAnimation()
    {
        var presentation = CompactOverlayPresentation.For(ActivationVisualState.Processing);

        Assert.Equal("Распознаю", presentation.Title);
        Assert.Equal(CompactOverlayActivity.Progress, presentation.Activity);
        Assert.DoesNotContain("GigaAM", presentation.Title, StringComparison.OrdinalIgnoreCase);
    }
}

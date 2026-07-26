namespace VoiceInput.Core.Activation;

public enum CompactOverlayActivity
{
    Meter,
    Progress,
}

public readonly record struct CompactOverlayPresentation(string Title, CompactOverlayActivity Activity)
{
    public static CompactOverlayPresentation For(ActivationVisualState state)
    {
        return state switch
        {
            ActivationVisualState.Listening =>
                new CompactOverlayPresentation("Слушаю", CompactOverlayActivity.Meter),
            ActivationVisualState.Processing =>
                new CompactOverlayPresentation("Распознаю", CompactOverlayActivity.Progress),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }
}

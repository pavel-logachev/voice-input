namespace VoiceInput.Core.Activation;

public readonly record struct InputTarget(nint WindowHandle, uint ProcessId)
{
    public bool IsValid => WindowHandle != nint.Zero;
}

public enum ActivationVisualState
{
    Listening,
    Processing,
    NoSpeech,
    Inserting,
    Success,
}

public interface IInputTargetCapture
{
    InputTarget Capture();
}

public interface IActivationOverlay
{
    void Show(ActivationVisualState state);

    void Hide();
}

public interface IModifierReleaseGate
{
    ValueTask WaitAsync(CancellationToken cancellationToken);
}

public interface ITextInserter
{
    ValueTask InsertAsync(
        InputTarget target,
        string text,
        CancellationToken cancellationToken);
}

public interface IAsyncDelay
{
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

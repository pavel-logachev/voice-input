namespace VoiceInput.Core.Activation;

public readonly record struct InputTarget(nint WindowHandle, uint ProcessId)
{
    public bool IsValid => WindowHandle != nint.Zero;
}

public enum ActivationVisualState
{
    Listening,
    Inserting,
    Success,
}

public enum FakeDictationWorkflowState
{
    Idle,
    Running,
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

public sealed class FakeDictationWorkflow(
    IInputTargetCapture targetCapture,
    IActivationOverlay overlay,
    IModifierReleaseGate releaseGate,
    ITextInserter textInserter,
    IAsyncDelay delay)
{
    private int isRunning;

    public FakeDictationWorkflowState State { get; private set; } = FakeDictationWorkflowState.Idle;

    public async Task ActivateAsync(string text, CancellationToken cancellationToken)
    {
        await TryActivateAsync(text, cancellationToken);
    }

    public async Task<bool> TryActivateAsync(string text, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref isRunning, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            var target = targetCapture.Capture();
            if (!target.IsValid)
            {
                return false;
            }

            State = FakeDictationWorkflowState.Running;
            try
            {
                overlay.Show(ActivationVisualState.Listening);
                await releaseGate.WaitAsync(cancellationToken);

                overlay.Show(ActivationVisualState.Inserting);
                await textInserter.InsertAsync(target, text, cancellationToken);

                overlay.Show(ActivationVisualState.Success);
                await delay.DelayAsync(TimeSpan.FromMilliseconds(350), cancellationToken);
                return true;
            }
            finally
            {
                overlay.Hide();
                State = FakeDictationWorkflowState.Idle;
            }
        }
        finally
        {
            Interlocked.Exchange(ref isRunning, 0);
        }
    }
}

using VoiceInput.Core.Activation;

namespace VoiceInput.Core.Tests.Activation;

public sealed class FakeDictationWorkflowTests
{
    [Fact]
    public async Task ActivationFollowsSafeTargetToInsertionOrder()
    {
        var trace = new List<string>();
        var target = new InputTarget((nint)42, 7);
        var workflow = new FakeDictationWorkflow(
            new RecordingTargetCapture(trace, target),
            new RecordingOverlay(trace),
            new RecordingReleaseGate(trace),
            new RecordingTextInserter(trace),
            new RecordingDelay(trace));

        await workflow.ActivateAsync("Диктовка работает.", CancellationToken.None);

        Assert.Equal(
            [
                "capture:42",
                "overlay:Listening",
                "gate:wait",
                "overlay:Inserting",
                "insert:42:Диктовка работает.",
                "overlay:Success",
                "delay:350",
                "overlay:hide",
            ],
            trace);
        Assert.Equal(FakeDictationWorkflowState.Idle, workflow.State);
    }

    [Fact]
    public async Task ConcurrentActivationIsIgnored()
    {
        var trace = new List<string>();
        var gate = new BlockingReleaseGate();
        var workflow = new FakeDictationWorkflow(
            new RecordingTargetCapture(trace, new InputTarget((nint)42, 7)),
            new RecordingOverlay(trace),
            gate,
            new RecordingTextInserter(trace),
            new RecordingDelay(trace));

        var firstActivation = workflow.TryActivateAsync("first", CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var secondAccepted = await workflow.TryActivateAsync("second", CancellationToken.None);

        Assert.False(secondAccepted);
        gate.Release.TrySetResult();
        Assert.True(await firstActivation);
        Assert.Single(trace, item => item.StartsWith("capture:", StringComparison.Ordinal));
        Assert.DoesNotContain(trace, item => item.EndsWith(":second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingTargetIsRejectedBeforeOverlayOrInsertion()
    {
        var trace = new List<string>();
        var workflow = new FakeDictationWorkflow(
            new RecordingTargetCapture(trace, default),
            new RecordingOverlay(trace),
            new RecordingReleaseGate(trace),
            new RecordingTextInserter(trace),
            new RecordingDelay(trace));

        var accepted = await workflow.TryActivateAsync("text", CancellationToken.None);

        Assert.False(accepted);
        Assert.Equal(["capture:0"], trace);
        Assert.Equal(FakeDictationWorkflowState.Idle, workflow.State);
    }

    private sealed class RecordingTargetCapture(List<string> trace, InputTarget target)
        : IInputTargetCapture
    {
        public InputTarget Capture()
        {
            trace.Add($"capture:{target.WindowHandle}");
            return target;
        }
    }

    private sealed class RecordingOverlay(List<string> trace) : IActivationOverlay
    {
        public void Show(ActivationVisualState state) => trace.Add($"overlay:{state}");

        public void Hide() => trace.Add("overlay:hide");
    }

    private sealed class RecordingReleaseGate(List<string> trace) : IModifierReleaseGate
    {
        public ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            trace.Add("gate:wait");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingReleaseGate : IModifierReleaseGate
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class RecordingTextInserter(List<string> trace) : ITextInserter
    {
        public ValueTask InsertAsync(
            InputTarget target,
            string text,
            CancellationToken cancellationToken)
        {
            trace.Add($"insert:{target.WindowHandle}:{text}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDelay(List<string> trace) : IAsyncDelay
    {
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            trace.Add($"delay:{delay.TotalMilliseconds:0}");
            return ValueTask.CompletedTask;
        }
    }
}

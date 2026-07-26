using VoiceInput.Core.Activation;
using VoiceInput.Core.Audio;
using VoiceInput.Core.Transcription;

namespace VoiceInput.Core.Tests.Activation;

public sealed class DictationWorkflowTests
{
    [Fact]
    public async Task ActivationRecordsTranscribesAndInsertsInSafeOrder()
    {
        var trace = new List<string>();
        var target = new InputTarget((nint)42, 7);
        var audio = new RecordedAudio([0.25f, -0.25f], 16_000);
        var workflow = new DictationWorkflow(
            new TargetCapture(trace, target),
            new Overlay(trace),
            new ReleaseGate(trace),
            new TextInserter(trace),
            new Delay(trace),
            new AudioRecorder(trace, audio),
            new Transcriber(trace, "Это локальная расшифровка."));

        var accepted = await workflow.TryActivateAsync(CancellationToken.None);

        Assert.True(accepted);
        Assert.Equal(
            [
                "capture:42",
                "overlay:Listening",
                "record:start",
                "gate:wait",
                "record:stop",
                "overlay:Processing",
                "transcribe:2",
                "overlay:Inserting",
                "insert:42:Это локальная расшифровка.",
                "overlay:Success",
                "delay:350",
                "overlay:hide",
            ],
            trace);
        Assert.Equal(DictationWorkflowState.Idle, workflow.State);
    }

    [Fact]
    public async Task ActivationCanUseASessionSpecificReleaseGate()
    {
        var trace = new List<string>();
        var manualGate = new ManualReleaseGate();
        var workflow = new DictationWorkflow(
            new TargetCapture(trace, new InputTarget((nint)42, 7)),
            new Overlay(trace),
            new ReleaseGate(trace),
            new TextInserter(trace),
            new Delay(trace),
            new AudioRecorder(trace, new RecordedAudio([0.25f], 16_000)),
            new Transcriber(trace, "Текст."));

        var activation = workflow.TryActivateAsync(manualGate, CancellationToken.None);

        Assert.False(activation.IsCompleted);
        Assert.Contains("record:start", trace);
        Assert.DoesNotContain("gate:wait", trace);

        manualGate.Release();
        Assert.True(await activation);
        Assert.Contains("record:stop", trace);
    }

    [Fact]
    public async Task ConcurrentActivationIsIgnored()
    {
        var trace = new List<string>();
        var gate = new BlockingReleaseGate();
        var workflow = new DictationWorkflow(
            new TargetCapture(trace, new InputTarget((nint)42, 7)),
            new Overlay(trace),
            gate,
            new TextInserter(trace),
            new Delay(trace),
            new AudioRecorder(trace, new RecordedAudio([0.25f], 16_000)),
            new Transcriber(trace, "Текст."));

        var first = workflow.TryActivateAsync(CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var second = await workflow.TryActivateAsync(CancellationToken.None);

        Assert.False(second);
        gate.Release.TrySetResult();
        Assert.True(await first);
        Assert.Single(trace, entry => entry.StartsWith("capture:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelActiveStopsRecordingWithoutTranscriptionOrInsertion()
    {
        var trace = new List<string>();
        var gate = new BlockingReleaseGate();
        var workflow = new DictationWorkflow(
            new TargetCapture(trace, new InputTarget((nint)42, 7)),
            new Overlay(trace),
            gate,
            new TextInserter(trace),
            new Delay(trace),
            new AudioRecorder(trace, new RecordedAudio([0.25f], 16_000)),
            new Transcriber(trace, "Не должно вставляться."));

        var activation = workflow.TryActivateAsync(CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(workflow.CancelActive());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activation);

        Assert.Contains("record:cancel", trace);
        Assert.DoesNotContain("record:stop", trace);
        Assert.DoesNotContain(trace, entry => entry.StartsWith("transcribe:", StringComparison.Ordinal));
        Assert.DoesNotContain(trace, entry => entry.StartsWith("insert:", StringComparison.Ordinal));
        Assert.Equal("overlay:hide", trace[^1]);
        Assert.Equal(DictationWorkflowState.Idle, workflow.State);
        Assert.False(workflow.CancelActive());
    }

    [Fact]
    public async Task CancelActiveDuringTranscriptionNeverInsertsLateText()
    {
        var trace = new List<string>();
        var transcriber = new BlockingTranscriber(trace, "Опоздавший текст.");
        var workflow = new DictationWorkflow(
            new TargetCapture(trace, new InputTarget((nint)42, 7)),
            new Overlay(trace),
            new ReleaseGate(trace),
            new TextInserter(trace),
            new Delay(trace),
            new AudioRecorder(trace, new RecordedAudio([0.25f], 16_000)),
            transcriber);

        var activation = workflow.TryActivateAsync(CancellationToken.None);
        await transcriber.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(workflow.CancelActive());
        transcriber.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activation);

        Assert.DoesNotContain(trace, entry => entry.StartsWith("insert:", StringComparison.Ordinal));
        Assert.Equal("overlay:hide", trace[^1]);
    }

    [Fact]
    public async Task CancellationIsRejectedAfterInsertionHasStarted()
    {
        var trace = new List<string>();
        var inserter = new BlockingTextInserter(trace);
        var workflow = new DictationWorkflow(
            new TargetCapture(trace, new InputTarget((nint)42, 7)),
            new Overlay(trace),
            new ReleaseGate(trace),
            inserter,
            new Delay(trace),
            new AudioRecorder(trace, new RecordedAudio([0.25f], 16_000)),
            new Transcriber(trace, "Готовый текст."));

        var activation = workflow.TryActivateAsync(CancellationToken.None);
        await inserter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(workflow.CancelActive());
        inserter.Release.TrySetResult();

        Assert.True(await activation);
        Assert.Contains("insert:42:Готовый текст.", trace);
    }

    [Fact]
    public async Task MissingTargetIsRejectedBeforeRecording()
    {
        var trace = new List<string>();
        var workflow = new DictationWorkflow(
            new TargetCapture(trace, default),
            new Overlay(trace),
            new ReleaseGate(trace),
            new TextInserter(trace),
            new Delay(trace),
            new AudioRecorder(trace, new RecordedAudio([0.25f], 16_000)),
            new Transcriber(trace, "Текст."));

        var accepted = await workflow.TryActivateAsync(CancellationToken.None);

        Assert.False(accepted);
        Assert.Equal(["capture:0"], trace);
        Assert.Equal(DictationWorkflowState.Idle, workflow.State);
    }

    private sealed class TargetCapture(List<string> trace, InputTarget target) : IInputTargetCapture
    {
        public InputTarget Capture()
        {
            trace.Add($"capture:{target.WindowHandle}");
            return target;
        }
    }

    private sealed class Overlay(List<string> trace) : IActivationOverlay
    {
        public void Show(ActivationVisualState state) => trace.Add($"overlay:{state}");

        public void Hide() => trace.Add("overlay:hide");
    }

    private sealed class ReleaseGate(List<string> trace) : IModifierReleaseGate
    {
        public ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            trace.Add("gate:wait");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingReleaseGate : IModifierReleaseGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class TextInserter(List<string> trace) : ITextInserter
    {
        public ValueTask InsertAsync(InputTarget target, string text, CancellationToken cancellationToken)
        {
            trace.Add($"insert:{target.WindowHandle}:{text}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingTextInserter(List<string> trace) : ITextInserter
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask InsertAsync(
            InputTarget target,
            string text,
            CancellationToken cancellationToken)
        {
            trace.Add($"insert:{target.WindowHandle}:{text}");
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class Delay(List<string> trace) : IAsyncDelay
    {
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            trace.Add($"delay:{delay.TotalMilliseconds:0}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AudioRecorder(List<string> trace, RecordedAudio audio) : IAudioRecorder
    {
        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            trace.Add("record:start");
            return ValueTask.CompletedTask;
        }

        public ValueTask<RecordedAudio> StopAsync(CancellationToken cancellationToken)
        {
            trace.Add("record:stop");
            return ValueTask.FromResult(audio);
        }

        public ValueTask CancelAsync()
        {
            trace.Add("record:cancel");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Transcriber(List<string> trace, string text) : ITranscriber
    {
        public ValueTask<string> TranscribeAsync(RecordedAudio audio, CancellationToken cancellationToken)
        {
            trace.Add($"transcribe:{audio.Samples.Length}");
            return ValueTask.FromResult(text);
        }
    }

    private sealed class BlockingTranscriber(List<string> trace, string text) : ITranscriber
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<string> TranscribeAsync(RecordedAudio audio, CancellationToken cancellationToken)
        {
            trace.Add($"transcribe:{audio.Samples.Length}");
            Entered.TrySetResult();
            await Release.Task;
            return text;
        }
    }
}

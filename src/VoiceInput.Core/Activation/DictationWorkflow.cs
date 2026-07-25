using VoiceInput.Core.Audio;
using VoiceInput.Core.Transcription;

namespace VoiceInput.Core.Activation;

public enum DictationWorkflowState
{
    Idle,
    Recording,
    Processing,
    Inserting,
}

public sealed class DictationWorkflow(
    IInputTargetCapture targetCapture,
    IActivationOverlay overlay,
    IModifierReleaseGate releaseGate,
    ITextInserter textInserter,
    IAsyncDelay delay,
    IAudioRecorder audioRecorder,
    ITranscriber transcriber)
{
    private int running;

    public DictationWorkflowState State { get; private set; } = DictationWorkflowState.Idle;

    public async Task<bool> TryActivateAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
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

            var recordingStarted = false;
            var recordingStopped = false;
            try
            {
                State = DictationWorkflowState.Recording;
                overlay.Show(ActivationVisualState.Listening);
                await audioRecorder.StartAsync(cancellationToken);
                recordingStarted = true;

                await releaseGate.WaitAsync(cancellationToken);
                var audio = await audioRecorder.StopAsync(cancellationToken);
                recordingStopped = true;

                State = DictationWorkflowState.Processing;
                overlay.Show(ActivationVisualState.Processing);
                var text = (await transcriber.TranscribeAsync(audio, cancellationToken)).Trim();

                if (text.Length == 0)
                {
                    overlay.Show(ActivationVisualState.NoSpeech);
                    await delay.DelayAsync(TimeSpan.FromMilliseconds(650), cancellationToken);
                    return true;
                }

                State = DictationWorkflowState.Inserting;
                overlay.Show(ActivationVisualState.Inserting);
                await textInserter.InsertAsync(target, text, cancellationToken);

                overlay.Show(ActivationVisualState.Success);
                await delay.DelayAsync(TimeSpan.FromMilliseconds(350), cancellationToken);
                return true;
            }
            finally
            {
                if (recordingStarted && !recordingStopped)
                {
                    await audioRecorder.CancelAsync();
                }

                overlay.Hide();
                State = DictationWorkflowState.Idle;
            }
        }
        finally
        {
            Interlocked.Exchange(ref running, 0);
        }
    }
}

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
    private readonly object sessionSync = new();
    private CancellationTokenSource? activeSession;
    private int running;

    public DictationWorkflowState State { get; private set; } = DictationWorkflowState.Idle;

    public bool CancelActive()
    {
        lock (sessionSync)
        {
            if (activeSession is null || State == DictationWorkflowState.Inserting)
            {
                return false;
            }

            activeSession.Cancel();
            return true;
        }
    }

    public async Task<bool> TryActivateAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            return false;
        }

        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (sessionSync)
        {
            activeSession = session;
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
                await audioRecorder.StartAsync(session.Token);
                recordingStarted = true;

                await releaseGate.WaitAsync(session.Token);
                var audio = await audioRecorder.StopAsync(session.Token);
                recordingStopped = true;

                State = DictationWorkflowState.Processing;
                overlay.Show(ActivationVisualState.Processing);
                var text = (await transcriber.TranscribeAsync(audio, session.Token)).Trim();
                session.Token.ThrowIfCancellationRequested();

                if (text.Length == 0)
                {
                    overlay.Show(ActivationVisualState.NoSpeech);
                    await delay.DelayAsync(TimeSpan.FromMilliseconds(650), session.Token);
                    return true;
                }

                State = DictationWorkflowState.Inserting;
                overlay.Show(ActivationVisualState.Inserting);
                await textInserter.InsertAsync(target, text, session.Token);

                overlay.Show(ActivationVisualState.Success);
                await delay.DelayAsync(TimeSpan.FromMilliseconds(350), session.Token);
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
            lock (sessionSync)
            {
                if (ReferenceEquals(activeSession, session))
                {
                    activeSession = null;
                }
            }

            Interlocked.Exchange(ref running, 0);
        }
    }
}

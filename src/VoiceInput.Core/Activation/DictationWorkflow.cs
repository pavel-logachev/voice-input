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

    /// <summary>
    /// True while a session can still be cancelled: it is recording or transcribing, not yet inserting text.
    /// </summary>
    public bool CanCancel
    {
        get
        {
            lock (sessionSync)
            {
                return activeSession is { IsCancellationRequested: false }
                    && State is DictationWorkflowState.Recording or DictationWorkflowState.Processing;
            }
        }
    }

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

    public Task<bool> TryActivateAsync(CancellationToken cancellationToken) =>
        TryActivateAsync(releaseGate, cancellationToken);

    public async Task<bool> TryActivateAsync(
        IModifierReleaseGate sessionReleaseGate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionReleaseGate);
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

                await WaitForReleaseOrCaptureEndAsync(sessionReleaseGate, session);
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
                try
                {
                    if (recordingStarted && !recordingStopped)
                    {
                        await audioRecorder.CancelAsync();
                    }
                }
                catch (Exception)
                {
                    // Cleanup is best-effort; the original workflow outcome stays authoritative.
                }
                finally
                {
                    try
                    {
                        overlay.Hide();
                    }
                    finally
                    {
                        State = DictationWorkflowState.Idle;
                    }
                }
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

    // Recording ends either when the user releases the hotkey or when the capture stops by itself
    // (device removed, recording limit reached). Whichever happens first stops the recording.
    private async Task WaitForReleaseOrCaptureEndAsync(
        IModifierReleaseGate sessionReleaseGate,
        CancellationTokenSource session)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        var released = sessionReleaseGate.WaitAsync(race.Token).AsTask();
        var captureEnded = audioRecorder.WaitForRecordingEndAsync(race.Token);
        Observe(released);
        Observe(captureEnded);
        try
        {
            var first = await Task.WhenAny(released, captureEnded).ConfigureAwait(false);
            session.Token.ThrowIfCancellationRequested();
            await first.ConfigureAwait(false);
        }
        finally
        {
            await race.CancelAsync().ConfigureAwait(false);
        }
    }

    private static void Observe(Task task) =>
        task.ContinueWith(
            static completed => completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}

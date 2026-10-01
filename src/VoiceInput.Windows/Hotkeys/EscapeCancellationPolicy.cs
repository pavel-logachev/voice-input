namespace VoiceInput.Windows.Hotkeys;

public enum EscapeDecisionKind
{
    /// <summary>No dictation is active: Escape belongs to the foreground application.</summary>
    PassThrough,

    /// <summary>First Escape of an active session: swallow it and cancel the dictation.</summary>
    QueueCancellation,

    /// <summary>Further Escape presses while a cancellation is already queued: swallow them too.</summary>
    Swallow,
}

public readonly record struct EscapeDecision(EscapeDecisionKind Kind, long Generation);

/// <summary>
/// Decides what to do with Escape from a low-level keyboard hook. The hook must answer immediately,
/// while the cancellation itself runs later on the UI thread. A generation counter makes sure a
/// cancellation queued for one session can never cancel the next one.
/// </summary>
public sealed class EscapeCancellationPolicy(Func<bool> canCancel)
{
    private readonly Func<bool> canCancel = canCancel ?? throw new ArgumentNullException(nameof(canCancel));
    private readonly object sync = new();
    private long generation;
    private bool armed;
    private long? queuedGeneration;

    public void BeginSession()
    {
        lock (sync)
        {
            armed = true;
            generation++;
            queuedGeneration = null;
        }
    }

    public void EndSession()
    {
        lock (sync)
        {
            armed = false;
            generation++;
            queuedGeneration = null;
        }
    }

    public EscapeDecision Decide()
    {
        if (!canCancel())
        {
            return new EscapeDecision(EscapeDecisionKind.PassThrough, 0);
        }

        lock (sync)
        {
            if (!armed)
            {
                return new EscapeDecision(EscapeDecisionKind.PassThrough, 0);
            }

            if (queuedGeneration is null)
            {
                queuedGeneration = generation;
                return new EscapeDecision(EscapeDecisionKind.QueueCancellation, generation);
            }

            return new EscapeDecision(EscapeDecisionKind.Swallow, 0);
        }
    }

    public bool TryConsumeQueuedCancellation(long expectedGeneration)
    {
        lock (sync)
        {
            if (!armed || expectedGeneration != generation || queuedGeneration != expectedGeneration)
            {
                return false;
            }

            queuedGeneration = null;
            return true;
        }
    }
}

using VoiceInput.Windows.Hotkeys;

namespace VoiceInput.Windows.Tests.Hotkeys;

public sealed class EscapeCancellationPolicyTests
{
    [Fact]
    public void EscapePassesThroughWhenNothingCanBeCancelled()
    {
        var policy = new EscapeCancellationPolicy(() => false);
        policy.BeginSession();

        Assert.Equal(EscapeDecisionKind.PassThrough, policy.Decide().Kind);
    }

    [Fact]
    public void EscapePassesThroughBeforeASessionIsArmed()
    {
        var policy = new EscapeCancellationPolicy(() => true);

        Assert.Equal(EscapeDecisionKind.PassThrough, policy.Decide().Kind);
    }

    [Fact]
    public void FirstEscapeQueuesCancellationAndFurtherPressesAreSwallowed()
    {
        var policy = new EscapeCancellationPolicy(() => true);
        policy.BeginSession();

        var first = policy.Decide();
        var second = policy.Decide();

        Assert.Equal(EscapeDecisionKind.QueueCancellation, first.Kind);
        Assert.Equal(EscapeDecisionKind.Swallow, second.Kind);
    }

    [Fact]
    public void QueuedCancellationIsConsumedExactlyOnce()
    {
        var policy = new EscapeCancellationPolicy(() => true);
        policy.BeginSession();
        var decision = policy.Decide();

        Assert.True(policy.TryConsumeQueuedCancellation(decision.Generation));
        Assert.False(policy.TryConsumeQueuedCancellation(decision.Generation));
    }

    [Fact]
    public void CancellationQueuedForAnEarlierSessionNeverCancelsTheNextOne()
    {
        var policy = new EscapeCancellationPolicy(() => true);
        policy.BeginSession();
        var stale = policy.Decide();
        policy.EndSession();
        policy.BeginSession();

        Assert.False(policy.TryConsumeQueuedCancellation(stale.Generation));
        Assert.Equal(EscapeDecisionKind.QueueCancellation, policy.Decide().Kind);
    }

    [Fact]
    public void CancellationDoesNotFireAfterTheSessionEnded()
    {
        var policy = new EscapeCancellationPolicy(() => true);
        policy.BeginSession();
        var decision = policy.Decide();
        policy.EndSession();

        Assert.False(policy.TryConsumeQueuedCancellation(decision.Generation));
        Assert.Equal(EscapeDecisionKind.PassThrough, policy.Decide().Kind);
    }

    [Fact]
    public void PolicyRequiresACancellationPredicate() =>
        Assert.Throws<ArgumentNullException>(() => new EscapeCancellationPolicy(null!));
}

using VoiceInput.Windows.Lifecycle;

namespace VoiceInput.Windows.Tests.Lifecycle;

public sealed class SingleInstanceLeaseTests
{
    [Fact]
    public void ASecondLeaseIsRejectedUntilThePrimaryLeaseIsDisposed()
    {
        var name = $"Local\\VoiceInput.Tests.{Guid.NewGuid():N}";
        using var first = new SingleInstanceLease(name);
        using var second = new SingleInstanceLease(name);

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);

        first.Dispose();
        using var replacement = new SingleInstanceLease(name);

        Assert.True(replacement.IsPrimary);
    }
}

namespace VoiceInput.Windows.Lifecycle;

public sealed class SingleInstanceLease : IDisposable
{
    private readonly Semaphore semaphore;
    private bool ownsLease;
    private bool disposed;

    public SingleInstanceLease(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        semaphore = new Semaphore(1, 1, name);
        ownsLease = semaphore.WaitOne(TimeSpan.Zero);
        IsPrimary = ownsLease;
    }

    public bool IsPrimary { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (ownsLease)
        {
            semaphore.Release();
            ownsLease = false;
        }

        semaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}

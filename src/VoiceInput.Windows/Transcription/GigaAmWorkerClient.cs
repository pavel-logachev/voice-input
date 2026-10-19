using VoiceInput.Core.Diagnostics;

namespace VoiceInput.Windows.Transcription;

public interface IAsrWorkerConnection : IAsyncDisposable
{
    bool HasExited { get; }
    Task StartAsync(CancellationToken cancellationToken);
    ValueTask<string> TranscribeAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken cancellationToken);
}

public sealed record WorkerRecoveryOptions
{
    public int MaximumRestarts { get; init; } = 3;
    public TimeSpan Backoff { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MinimumRequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public double TimeoutPerAudioSecond { get; init; } = 3;
}

public enum WorkerState { Stopped, Starting, Ready, Failed, RestartLimitReached }

/// <summary>Serializes requests and invalidates the whole connection on cancellation, crash or deadline.
/// A failed dictation is never replayed; recovery happens on the next request.</summary>
public sealed class GigaAmWorkerClient : IAsrSegmentClient, IAsyncDisposable, IForceStop
{
    private readonly Func<IAsrWorkerConnection> connectionFactory;
    private readonly WorkerRecoveryOptions options;
    private readonly SemaphoreSlim exchangeLock = new(1, 1);
    private IAsrWorkerConnection? connection;
    private int failures;
    private bool disposed;

    public GigaAmWorkerClient(string workerExecutable, string runtimeDirectory, string modelPath)
        : this(() => new NamedPipeWorkerConnection(workerExecutable, runtimeDirectory, modelPath)) { }

    public GigaAmWorkerClient(Func<IAsrWorkerConnection> connectionFactory, WorkerRecoveryOptions? options = null)
    {
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        this.options = options ?? new WorkerRecoveryOptions();
        if (this.options.MaximumRestarts < 0 || this.options.Backoff < TimeSpan.Zero
            || this.options.StartupTimeout <= TimeSpan.Zero || this.options.MinimumRequestTimeout <= TimeSpan.Zero
            || this.options.TimeoutPerAudioSecond <= 0) throw new ArgumentOutOfRangeException(nameof(options));
    }

    public WorkerState State { get; private set; }
    public TranscriptionErrorCode? LastError { get; private set; }
    public event EventHandler<WorkerState>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await exchangeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await EnsureStartedAsync(cancellationToken).ConfigureAwait(false); }
        finally { exchangeLock.Release(); }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (connection is { HasExited: false }) return;
        if (connection is not null)
        {
            await ResetAsync().ConfigureAwait(false);
            failures++;
        }
        if (failures > options.MaximumRestarts)
        {
            LastError = TranscriptionErrorCode.WorkerRestartLimit;
            SetState(WorkerState.RestartLimitReached);
            throw new TranscriptionException(LastError.Value, VoiceInputStrings.TranscriptionError(LastError.Value));
        }
        if (failures > 0) await Task.Delay(options.Backoff * failures, cancellationToken).ConfigureAwait(false);
        SetState(WorkerState.Starting);
        connection = connectionFactory();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.StartupTimeout);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await connection.StartAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            EngineDiagnostics.WriteTiming("model-load", watch.Elapsed);
            LastError = null;
            SetState(WorkerState.Ready);
        }
        catch (Exception exception)
        {
            await ResetAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) SetState(WorkerState.Stopped);
            cancellationToken.ThrowIfCancellationRequested();
            failures++;
            LastError = exception is OperationCanceledException ? TranscriptionErrorCode.WorkerTimeout : TranscriptionErrorCode.WorkerExited;
            SetState(WorkerState.Failed);
            throw new TranscriptionException(LastError.Value, VoiceInputStrings.TranscriptionError(LastError.Value), exception);
        }
    }

    public async ValueTask<string> TranscribeSegmentAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sampleRate != 16000 || samples.Length is <= 0 or > 400000) throw new ArgumentOutOfRangeException(nameof(samples));
        await exchangeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(options.MinimumRequestTimeout + TimeSpan.FromSeconds(samples.Length / (double)sampleRate * options.TimeoutPerAudioSecond));
            try
            {
                var text = await connection!.TranscribeAsync(samples, sampleRate, deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
                failures = 0;
                LastError = null;
                return text;
            }
            catch (Exception exception)
            {
                await ResetAsync().ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                { SetState(WorkerState.Stopped); throw new OperationCanceledException(cancellationToken); }
                failures++;
                LastError = exception is OperationCanceledException ? TranscriptionErrorCode.WorkerTimeout
                    : exception is InvalidDataException ? TranscriptionErrorCode.WorkerProtocol : TranscriptionErrorCode.WorkerExited;
                SetState(WorkerState.Failed);
                throw new TranscriptionException(LastError.Value, VoiceInputStrings.TranscriptionError(LastError.Value), exception);
            }
        }
        finally { exchangeLock.Release(); }
    }

    private void SetState(WorkerState state) { State = state; StateChanged?.Invoke(this, state); }
    private async ValueTask ResetAsync()
    {
        if (connection is null) return;
        var old = connection;
        connection = null;
        await old.DisposeAsync().ConfigureAwait(false);
    }

    public void ForceStop()
    {
        if (connection is IForceStop worker) worker.ForceStop();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await exchangeLock.WaitAsync().ConfigureAwait(false);
        try { await ResetAsync().ConfigureAwait(false); SetState(WorkerState.Stopped); }
        finally { exchangeLock.Release(); }
        GC.SuppressFinalize(this);
    }
}

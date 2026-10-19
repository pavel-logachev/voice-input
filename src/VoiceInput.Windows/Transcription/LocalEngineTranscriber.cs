using VoiceInput.Core.Audio;
using VoiceInput.Core.Transcription;

namespace VoiceInput.Windows.Transcription;

/// <summary>Model lookup is deferred until dictation; selecting Local never downloads anything.</summary>
public sealed class LocalEngineTranscriber : ITranscriber, IAsyncDisposable, ILocalModelLifetime, IForceStop
{
    private readonly Func<(IAsrSegmentClient Client, IAsyncDisposable Lifetime)> createClient;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IAsyncDisposable? clientLifetime;
    private SegmentingTranscriber? transcriber;
    private readonly CancellationTokenSource shutdown = new();
    private bool disposed;

    public void ForceStop()
    {
        if (clientLifetime is IForceStop worker) worker.ForceStop();
    }

    public LocalEngineTranscriber(string workerExecutable)
    {
        createClient = () =>
        {
            var assets = new LocalAsrAssetProvisioner().GetInstalledAssets();
            LocalModelLifetimes.Register(assets.ModelPath, this);
            var client = new GigaAmWorkerClient(workerExecutable, assets.RuntimeDirectory, assets.ModelPath);
            return (client, client);
        };
        LocalModelLifetimes.Register(Path.Combine(Settings.AppDataPaths.Root, "models", LocalModelManager.DefaultModel.FileName), this);
    }

    public LocalEngineTranscriber(Func<(IAsrSegmentClient Client, IAsyncDisposable Lifetime)> createClient, string? modelPath = null)
    {
        this.createClient = createClient ?? throw new ArgumentNullException(nameof(createClient));
        if (modelPath is not null) LocalModelLifetimes.Register(modelPath, this);
    }

    public async Task<bool> TryWithModelReleasedAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return false;
        try
        {
            if (clientLifetime is not null) await clientLifetime.DisposeAsync().ConfigureAwait(false);
            clientLifetime = null;
            transcriber = null;
            await operation().ConfigureAwait(false);
            return true;
        }
        finally { gate.Release(); }
    }

    public async ValueTask<string> TranscribeAsync(RecordedAudio audio, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
        await gate.WaitAsync(session.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (transcriber is null)
            {
                var created = createClient();
                clientLifetime = created.Lifetime;
                transcriber = new SegmentingTranscriber(created.Client);
            }
            return await transcriber!.TranscribeAsync(audio, session.Token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await shutdown.CancelAsync().ConfigureAwait(false);
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false))
        {
            ForceStop();
            return;
        }
        try
        {
            if (clientLifetime is not null) await clientLifetime.DisposeAsync().ConfigureAwait(false);
            clientLifetime = null;
            transcriber = null;
        }
        finally { gate.Release(); }
    }
}

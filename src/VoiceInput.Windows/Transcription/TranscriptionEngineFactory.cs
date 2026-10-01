using VoiceInput.Core.Transcription;
using VoiceInput.Windows.Settings;

namespace VoiceInput.Windows.Transcription;

/// <summary>A ready transcriber together with whatever must be released when the engine is replaced.</summary>
public sealed class TranscriptionEngineHandle(ITranscriber transcriber, IAsyncDisposable? resources = null) : IAsyncDisposable
{
    public ITranscriber Transcriber { get; } = transcriber ?? throw new ArgumentNullException(nameof(transcriber));

    public async ValueTask DisposeAsync()
    {
        if (resources is not null)
        {
            await resources.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>Builds the transcriber for the engine chosen in the settings.</summary>
public sealed class TranscriptionEngineFactory(
    DpapiOpenAiApiKeyProvider apiKeyProvider,
    string workerExecutablePath)
{
    public static string DefaultWorkerExecutablePath { get; } = Path.Combine(
        AppContext.BaseDirectory,
        "worker",
        "VoiceInput.Asr.Worker.exe");

    public bool IsOpenAiConfigured => apiKeyProvider.HasKey;

    /// <summary>The local engine needs its worker next to the application; ordinary installs ship without it.</summary>
    public bool IsLocalEngineAvailable => File.Exists(workerExecutablePath);

    public async Task<TranscriptionEngineHandle> CreateAsync(
        AppSettings settings,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Engine switch
        {
            TranscriptionEngine.OpenAi => CreateOpenAi(settings),
            TranscriptionEngine.Local => await CreateLocalAsync(progress, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.Engine, null),
        };
    }

    private TranscriptionEngineHandle CreateOpenAi(AppSettings settings)
    {
        // Fail at startup with a clear message instead of on the first dictation.
        _ = apiKeyProvider.GetApiKey();
        var httpClient = new HttpClient { Timeout = OpenAiTranscriber.RecommendedRequestTimeout };
        var transcriber = new OpenAiTranscriber(httpClient, apiKeyProvider, language: settings.Language);
        return new TranscriptionEngineHandle(transcriber, new DisposableAdapter(httpClient));
    }

    private async Task<TranscriptionEngineHandle> CreateLocalAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var assets = await new LocalAsrAssetProvisioner().EnsureAsync(progress, cancellationToken).ConfigureAwait(false);
        var client = new GigaAmWorkerClient(workerExecutablePath, assets.RuntimeDirectory, assets.ModelPath);
        try
        {
            progress?.Report("Запускаю локальную модель…");
            await client.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new TranscriptionEngineHandle(new SegmentingTranscriber(client), client);
    }

    private sealed class DisposableAdapter(IDisposable inner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

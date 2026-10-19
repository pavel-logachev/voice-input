using VoiceInput.Core.Transcription;
using VoiceInput.Windows.Settings;

namespace VoiceInput.Windows.Transcription;

/// <summary>A ready transcriber together with whatever must be released when the engine is replaced.</summary>
public sealed class TranscriptionEngineHandle(ITranscriber transcriber, IAsyncDisposable? resources = null) : IAsyncDisposable, IForceStop
{
    public ITranscriber Transcriber { get; } = transcriber ?? throw new ArgumentNullException(nameof(transcriber));

    public void ForceStop() { if (resources is IForceStop worker) worker.ForceStop(); }

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
    public static string DefaultWorkerExecutablePath => File.Exists(Path.Combine(AppContext.BaseDirectory, "VoiceInput.Asr.Worker.exe"))
        ? Path.Combine(AppContext.BaseDirectory, "VoiceInput.Asr.Worker.exe")
        : Path.Combine(AppContext.BaseDirectory, "worker", "VoiceInput.Asr.Worker.exe");

    public bool IsOpenAiConfigured => apiKeyProvider.HasKey;

    /// <summary>The local engine is selectable even before its model is downloaded.</summary>
    public bool IsLocalEngineAvailable => File.Exists(workerExecutablePath)
        && File.Exists(Path.Combine(Environment.GetEnvironmentVariable("VOICE_INPUT_TRANSCRIBE_RUNTIME")
            ?? Path.Combine(AppContext.BaseDirectory, "runtime", LocalAsrAssetProvisioner.RuntimeFolderName), "transcribe.dll"));

    public async Task<TranscriptionEngineHandle> CreateAsync(
        AppSettings settings,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Engine switch
        {
            TranscriptionEngine.OpenAi => CreateOpenAi(settings),
            TranscriptionEngine.CustomOpenAiCompatible => CreateCustom(settings),
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

    private static TranscriptionEngineHandle CreateCustom(AppSettings settings)
    {
        if (EngineStatus.Evaluate(settings, true, true) == EngineReadiness.CustomNotConfigured)
            throw new TranscriptionException(TranscriptionErrorCode.CustomNotConfigured,
                VoiceInputStrings.TranscriptionError(TranscriptionErrorCode.CustomNotConfigured));
        var baseUri = CustomEndpointPolicy.NormalizeBaseUrl(settings.CustomBaseUrl ?? string.Empty);
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = OpenAiTranscriber.RecommendedRequestTimeout };
        var transcriber = new OpenAiTranscriber(http, new DpapiCustomApiKeyProvider(),
            new Uri(baseUri, "audio/transcriptions"), model: settings.CustomModel!, language: settings.Language, customProvider: true);
        return new TranscriptionEngineHandle(transcriber, new DisposableAdapter(http));
    }

    private Task<TranscriptionEngineHandle> CreateLocalAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsLocalEngineAvailable)
            throw new TranscriptionException(TranscriptionErrorCode.WorkerExited,
                VoiceInputStrings.TranscriptionError(TranscriptionErrorCode.WorkerExited));
        var local = new LocalEngineTranscriber(workerExecutablePath);
        return Task.FromResult(new TranscriptionEngineHandle(local, local));
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

using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.App;

internal enum CustomKeyState
{
    None,
    Saved,
    Unreadable,
}

/// <summary>The local model as the settings window sees it.</summary>
internal interface ILocalModelHost
{
    LocalModelProgress Progress { get; }

    long ModelSizeBytes { get; }

    /// <summary>Raised on any thread; the window marshals it to its dispatcher.</summary>
    event EventHandler<LocalModelProgress>? ProgressChanged;

    /// <summary>Starts the download, or resumes it from the partial file.</summary>
    void Start();

    /// <summary>Stops the transfer and keeps the partial file for a later resume.</summary>
    void Pause();

    /// <summary>Deletes the model; the manager itself releases the local worker first.</summary>
    Task<ModelDeleteResult> DeleteAsync();
}

/// <summary>The user's own OpenAI-compatible server: its optional key and the connection check.</summary>
internal interface ICustomServerHost
{
    CustomKeyState KeyState { get; }

    /// <summary>Stores the key; returns a user-facing error message, or null on success.</summary>
    string? SaveKey(string apiKey);

    void DeleteKey();

    /// <summary>Asks the server for its models with the typed key or, when none is typed, the saved one.</summary>
    Task<CustomProviderValidationResult> CheckAsync(string baseUrl, string model, string? typedKey);

    void CancelCheck() { }
}

/// <summary>
/// Owns the model download and the custom-server key for the whole application run,
/// so a download keeps going after the settings window is closed.
/// </summary>
internal sealed class EngineSettingsServices : ILocalModelHost, ICustomServerHost, IAsyncDisposable
{
    private readonly LocalModelManager manager;
    private readonly DpapiCustomApiKeyProvider customKey;
    private readonly HttpMessageHandler? checkHandler;
    private readonly CancellationTokenSource lifetime = new();
    private LocalModelProgress progress;
    private int integrityChecked;
    private bool disposed;
    private CancellationTokenSource? checkCancellation;

    private readonly object checkGate = new();
    public void CancelCheck() { lock (checkGate) { checkCancellation?.Cancel(); } }

    public EngineSettingsServices(
        LocalModelManager? manager = null,
        DpapiCustomApiKeyProvider? customKey = null,
        HttpMessageHandler? checkHandler = null)
    {
        this.manager = manager ?? new LocalModelManager();
        this.customKey = customKey ?? new DpapiCustomApiKeyProvider();
        this.checkHandler = checkHandler;
        progress = WithPartialFile(this.manager.Progress);
        this.manager.ProgressChanged += OnManagerProgress;
    }

    public event EventHandler<LocalModelProgress>? ProgressChanged;

    public LocalModelProgress Progress
    {
        get
        {
            VerifyInstalledOnce();
            return progress;
        }
    }

    public long ModelSizeBytes => manager.Model.SizeBytes;

    public CustomKeyState KeyState
    {
        get
        {
            if (!customKey.HasKey)
            {
                return CustomKeyState.None;
            }

            try
            {
                _ = customKey.GetApiKey();
                return CustomKeyState.Saved;
            }
            catch (InvalidOperationException)
            {
                return CustomKeyState.Unreadable;
            }
        }
    }

    public void Start()
    {
        if (disposed)
        {
            return;
        }

        _ = RunDownloadAsync();
    }

    public void Pause() => manager.Cancel();

    public Task<ModelDeleteResult> DeleteAsync() => manager.DeleteAsync();

    public string? SaveKey(string apiKey)
    {
        try
        {
            customKey.Save(apiKey);
            return null;
        }
        catch (ArgumentException)
        {
            return VoiceInputStrings.Get("custom.key.error.invalidChars");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return VoiceInputStrings.Get("custom.key.error.saveFailed");
        }
    }

    public void DeleteKey()
    {
        try
        {
            customKey.Delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The state line keeps showing the saved key, which is the truth.
        }
    }

    public async Task<CustomProviderValidationResult> CheckAsync(string baseUrl, string model, string? typedKey)
    {
        if (!DpapiCustomApiKeyProvider.IsValidKey(typedKey))
            return new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.InvalidKeyCharacters);
        CancelCheck();
        using var check = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        lock (checkGate) { checkCancellation = check; }
        string key;
        try
        {
            key = string.IsNullOrWhiteSpace(typedKey) ? customKey.GetApiKey() : typedKey;
        }
        catch (InvalidOperationException)
        {
            // An unreadable saved key: ask without one; the key line already says what to do.
            key = string.Empty;
        }

        using var http = checkHandler is null
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            : new HttpClient(checkHandler, disposeHandler: false);
        try
        {
            return await CustomProviderValidation.ValidateAsync(http, baseUrl, model, key, check.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return new CustomProviderValidationResult(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.InvalidRequest);
        }
        finally { lock (checkGate) { if (ReferenceEquals(checkCancellation, check)) checkCancellation = null; } }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        manager.ProgressChanged -= OnManagerProgress;
        await lifetime.CancelAsync().ConfigureAwait(false);
        await manager.DisposeAsync().ConfigureAwait(false);
        lifetime.Dispose();
    }

    private async Task RunDownloadAsync()
    {
        try
        {
            await manager.StartAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The manager reports its own failures; this only covers what it does not expect.
            Publish(new LocalModelProgress(LocalModelState.Failed, FailureReason: ModelFailureReason.Network));
        }
    }

    // A file that is present is trusted at once; its hash is checked in the background the first time it is shown.
    private void VerifyInstalledOnce()
    {
        if (progress.State != LocalModelState.Ready || Interlocked.Exchange(ref integrityChecked, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (!await manager.QueryInstalledAsync(lifetime.Token) && progress.State == LocalModelState.Ready && manager.IsInstalled)
                {
                    Publish(new LocalModelProgress(LocalModelState.Failed, FailureReason: ModelFailureReason.HashMismatch));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // Busy or gone: the next download or dictation will say so.
            }
        });
    }

    private void OnManagerProgress(object? sender, LocalModelProgress next) => Publish(WithPartialFile(next));

    private void Publish(LocalModelProgress next)
    {
        progress = next;
        ProgressChanged?.Invoke(this, next);
    }

    // After a restart the manager knows only that a partial file exists; the window shows how much of it there is.
    private LocalModelProgress WithPartialFile(LocalModelProgress value)
    {
        if (value.State != LocalModelState.Paused || value.Bytes > 0)
        {
            return value;
        }

        try
        {
            var partial = new FileInfo(manager.TemporaryPath);
            return partial.Exists ? value with { Bytes = partial.Length, TotalBytes = value.TotalBytes ?? manager.Model.SizeBytes } : value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return value;
        }
    }
}

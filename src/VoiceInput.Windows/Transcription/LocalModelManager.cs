using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using VoiceInput.Windows.Settings;

namespace VoiceInput.Windows.Transcription;

public enum LocalModelState { NotInstalled, Downloading, Paused, Verifying, Ready, Failed }
public enum ModelFailureReason { Network, InsufficientDiskSpace, HashMismatch, FileAccess, InvalidResponse }
public sealed record LocalModelProgress(LocalModelState State, long Bytes = 0, long? TotalBytes = null,
    double BytesPerSecond = 0, ModelFailureReason? FailureReason = null);
public sealed record LocalModelDefinition(LocalModelVariant Variant, string FileName, Uri DownloadUri, string Sha256, long SizeBytes);

/// <summary>UI-independent, single-download model store. Cancel pauses and preserves the Range checkpoint.</summary>
public sealed class LocalModelManager : IAsyncDisposable
{
    public static LocalModelDefinition DefaultModel { get; } = new(LocalModelVariant.GigaAmV3E2eRnntQ4,
        "gigaam-v3-e2e-rnnt-Q4_K_M.gguf",
        new Uri("https://huggingface.co/handy-computer/gigaam-v3-e2e-rnnt-gguf/resolve/main/gigaam-v3-e2e-rnnt-Q4_K_M.gguf?download=true"),
        "7d69952fb431a8d7800ed9910dc61fea37d8406bfe96d10bf24c8bd4b7c68623", 183_948_704);
    public static IReadOnlyList<LocalModelDefinition> Models { get; } = [DefaultModel];

    private readonly HttpClient httpClient;
    private readonly bool ownsClient;
    private readonly Func<string, long> availableSpace;
    private readonly TimeSpan readIdleTimeout;
    private readonly object gate = new();
    private CancellationTokenSource? activeCancellation;
    private Task? activeTask;
    private bool disposed;
    private bool deleting;

    public LocalModelManager(string? dataDirectory = null, HttpClient? httpClient = null,
        Func<string, long>? availableSpace = null, LocalModelDefinition? model = null, TimeSpan? readIdleTimeout = null)
    {
        DataDirectory = Path.GetFullPath(dataDirectory ?? AppDataPaths.Root);
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        ownsClient = httpClient is null;
        // HttpClient.Timeout stops at headers with ResponseHeadersRead. Bound every body read separately.
        var configured = readIdleTimeout ?? TimeSpan.FromSeconds(30);
        this.readIdleTimeout = this.httpClient.Timeout > TimeSpan.Zero && this.httpClient.Timeout < configured
            ? this.httpClient.Timeout : configured;
        if (this.readIdleTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(readIdleTimeout));
        this.availableSpace = availableSpace ?? (path => new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace);
        Model = model ?? DefaultModel;
        Progress = new LocalModelProgress(IsInstalled ? LocalModelState.Ready
            : File.Exists(TemporaryPath) ? LocalModelState.Paused : LocalModelState.NotInstalled);
    }

    public string DataDirectory { get; }
    public LocalModelDefinition Model { get; }
    public string ModelPath => Path.Combine(DataDirectory, "models", Model.FileName);
    public string TemporaryPath => ModelPath + ".download";
    public bool IsInstalled => File.Exists(ModelPath);
    public LocalModelProgress Progress { get; private set; }
    /// <summary>Raised on the download thread, at most ten times per second while transferring. Marshal to the WPF dispatcher.</summary>
    public event EventHandler<LocalModelProgress>? ProgressChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (deleting) return Task.FromException(new InvalidOperationException(VoiceInputStrings.Get("local.delete.inUse")));
            if (activeTask is { IsCompleted: false }) return activeTask;
            activeCancellation?.Dispose();
            activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            activeTask = Task.Run(() => DownloadAsync(activeCancellation.Token), CancellationToken.None);
            return activeTask;
        }
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default) => StartAsync(cancellationToken);
    public void Cancel() { lock (gate) { activeCancellation?.Cancel(); } }

    public Task<ModelDeleteResult> DeleteAsync() => DeleteCoreAsync(null);

    /// <summary>Compatibility overload for callers that report busy/file-access through an exception.</summary>
    public async Task DeleteAsync(ILocalModelLifetime engine, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await DeleteAsync(engine).ConfigureAwait(false);
        if (result != ModelDeleteResult.Deleted) throw new IOException(VoiceInputStrings.Get("local.delete.inUse"));
    }

    public Task<ModelDeleteResult> DeleteAsync(ILocalModelLifetime engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return DeleteCoreAsync(engine);
    }

    private async Task<ModelDeleteResult> DeleteCoreAsync(ILocalModelLifetime? engine)
    {
        lock (gate)
        {
            if (deleting || activeTask is { IsCompleted: false }) return ModelDeleteResult.InUse;
            deleting = true;
        }
        try
        {
            Task DeleteFiles()
            {
                File.Delete(ModelPath);
                File.Delete(TemporaryPath);
                Publish(new(LocalModelState.NotInstalled));
                return Task.CompletedTask;
            }
            var released = engine is null
                ? await LocalModelLifetimes.TryReleaseAsync(ModelPath, DeleteFiles).ConfigureAwait(false)
                : await engine.TryWithModelReleasedAsync(DeleteFiles).ConfigureAwait(false);
            return released ? ModelDeleteResult.Deleted : ModelDeleteResult.InUse;
        }
        catch (IOException) { return ModelDeleteResult.FileAccess; }
        catch (UnauthorizedAccessException) { return ModelDeleteResult.FileAccess; }
        finally { lock (gate) { deleting = false; } }
    }

    public async Task<bool> QueryInstalledAsync(CancellationToken cancellationToken = default)
    {
        var ready = await AssetIntegrity.VerifySha256Async(ModelPath, Model.Sha256, cancellationToken).ConfigureAwait(false);
        return ready;
    }

    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
            if (IsInstalled)
            {
                Publish(new(LocalModelState.Verifying));
                if (await QueryInstalledAsync(cancellationToken).ConfigureAwait(false))
                { Publish(new(LocalModelState.Ready, Model.SizeBytes, Model.SizeBytes)); return; }
                File.Delete(ModelPath);
            }
            // A bad checkpoint/hash gets exactly one clean retry, never an unbounded download loop.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var offset = File.Exists(TemporaryPath) ? new FileInfo(TemporaryPath).Length : 0;
                if (availableSpace(DataDirectory) < Math.Max(0, Model.SizeBytes - offset) + 8 * 1024 * 1024)
                { Publish(new(LocalModelState.Failed, offset, Model.SizeBytes, FailureReason: ModelFailureReason.InsufficientDiskSpace)); return; }
                Publish(new(LocalModelState.Downloading, offset, Model.SizeBytes));
                using var request = new HttpRequestMessage(HttpMethod.Get, Model.DownloadUri);
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    if (offset == Model.SizeBytes && await VerifyAndPromoteAsync(cancellationToken).ConfigureAwait(false)) return;
                    File.Delete(TemporaryPath);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    if (response.Content.Headers.ContentRange?.From != offset)
                    { Publish(new(LocalModelState.Failed, FailureReason: ModelFailureReason.InvalidResponse)); return; }
                }
                var reclaimedBytes = response.StatusCode == HttpStatusCode.PartialContent ? 0 : offset;
                if (response.StatusCode != HttpStatusCode.PartialContent) offset = 0; // Truncate ignored Range.
                var total = response.Content.Headers.ContentRange?.Length
                    ?? (response.Content.Headers.ContentLength is { } length ? offset + length : Model.SizeBytes);
                if (availableSpace(DataDirectory) < Math.Max(0, total - offset - reclaimedBytes) + 8 * 1024 * 1024)
                { Publish(new(LocalModelState.Failed, offset, total, FailureReason: ModelFailureReason.InsufficientDiskSpace)); return; }
                var watch = Stopwatch.StartNew();
                var received = offset;
                var lastReport = TimeSpan.Zero;
                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var target = new FileStream(TemporaryPath, offset > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[128 * 1024];
                    while (true)
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        idle.CancelAfter(readIdleTimeout);
                        var count = await source.ReadAsync(buffer, idle.Token).AsTask().WaitAsync(idle.Token).ConfigureAwait(false);
                        if (count == 0) break;
                        await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                        received += count;
                        if (watch.Elapsed - lastReport >= TimeSpan.FromMilliseconds(100))
                        {
                            Publish(new(LocalModelState.Downloading, received, total, (received - offset) / watch.Elapsed.TotalSeconds));
                            lastReport = watch.Elapsed;
                        }
                    }
                }
                if (await VerifyAndPromoteAsync(cancellationToken).ConfigureAwait(false)) return;
                File.Delete(TemporaryPath);
            }
            Publish(new(LocalModelState.Failed, FailureReason: ModelFailureReason.HashMismatch));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { Publish(new(LocalModelState.Paused, File.Exists(TemporaryPath) ? new FileInfo(TemporaryPath).Length : 0, Model.SizeBytes)); }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        { Publish(new(LocalModelState.Failed, FailureReason: ModelFailureReason.Network)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { Publish(new(LocalModelState.Failed, FailureReason: ModelFailureReason.FileAccess)); }
    }

    private async Task<bool> VerifyAndPromoteAsync(CancellationToken cancellationToken)
    {
        Publish(new(LocalModelState.Verifying, File.Exists(TemporaryPath) ? new FileInfo(TemporaryPath).Length : 0, Model.SizeBytes));
        if (!await AssetIntegrity.VerifySha256Async(TemporaryPath, Model.Sha256, cancellationToken).ConfigureAwait(false)) return false;
        File.Move(TemporaryPath, ModelPath, overwrite: true);
        Publish(new(LocalModelState.Ready, new FileInfo(ModelPath).Length, new FileInfo(ModelPath).Length));
        return true;
    }

    private void Publish(LocalModelProgress progress)
    {
        Progress = progress;
        ProgressChanged?.Invoke(this, progress);
    }

    public async ValueTask DisposeAsync()
    {
        Task? pending;
        lock (gate) { if (disposed) return; disposed = true; activeCancellation?.Cancel(); pending = activeTask; }
        if (pending is not null) await pending.ConfigureAwait(false);
        activeCancellation?.Dispose();
        if (ownsClient) httpClient.Dispose();
    }
}

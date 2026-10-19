using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using VoiceInput.Windows.Settings;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests;

public sealed class LocalModelManagerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "unit-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Payload = "known model bytes"u8.ToArray();
    private static LocalModelDefinition Definition => new(LocalModelVariant.GigaAmV3E2eRnntQ4, "test.gguf", new Uri("https://example.com/model"), Convert.ToHexString(SHA256.HashData(Payload)), Payload.Length);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResumeAppendsRangeOrRestartsWhenIgnored(bool supportsRange)
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(4, request.Headers.Range!.Ranges.Single().From);
            var response = new HttpResponseMessage(supportsRange ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            { Content = new ByteArrayContent(supportsRange ? Payload[4..] : Payload) };
            if (supportsRange) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(4, Payload.Length - 1, Payload.Length);
            return response;
        }));
        await using var manager = new LocalModelManager(directory, http, _ => long.MaxValue, Definition);
        Directory.CreateDirectory(Path.GetDirectoryName(manager.ModelPath)!);
        await File.WriteAllBytesAsync(manager.TemporaryPath, Payload[..4]);
        await manager.ResumeAsync();
        Assert.Equal(LocalModelState.Ready, manager.Progress.State);
        Assert.True(await manager.QueryInstalledAsync());
        Assert.Equal(Payload, await File.ReadAllBytesAsync(manager.ModelPath));
        await manager.DeleteAsync();
        Assert.False(manager.IsInstalled);
    }

    [Fact]
    public async Task BadHashRetriesOnceFromZeroThenFails()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            Assert.Null(request.Headers.Range);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent("bad"u8.ToArray()) };
        }));
        await using var manager = new LocalModelManager(directory, http, _ => long.MaxValue, Definition);
        await manager.StartAsync();
        Assert.Equal(2, calls);
        Assert.Equal(ModelFailureReason.HashMismatch, manager.Progress.FailureReason);
        Assert.False(manager.IsInstalled);
        Assert.False(File.Exists(manager.TemporaryPath));
    }

    [Fact]
    public async Task BadCheckpointHashRestartsFromZeroAndRecovers()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.Equal(4, request.Headers.Range!.Ranges.Single().From);
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(Payload[4..]) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(4, Payload.Length - 1, Payload.Length);
                return response;
            }
            Assert.Null(request.Headers.Range);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
        }));
        await using var manager = new LocalModelManager(directory, http, _ => long.MaxValue, Definition);
        Directory.CreateDirectory(Path.GetDirectoryName(manager.ModelPath)!);
        await File.WriteAllBytesAsync(manager.TemporaryPath, "oops"u8.ToArray());
        await manager.ResumeAsync();
        Assert.Equal(2, calls);
        Assert.True(await manager.QueryInstalledAsync());
        Assert.Equal(LocalModelState.Ready, manager.Progress.State);
    }

    [Fact]
    public async Task DiskSpaceFailureDoesNotStartNetworkRequest()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Network must not be used")));
        await using var manager = new LocalModelManager(directory, http, _ => 0, Definition);
        await manager.StartAsync();
        Assert.Equal(ModelFailureReason.InsufficientDiskSpace, manager.Progress.FailureReason);
    }

    [Fact]
    public async Task CancellationKeepsCheckpointAndResumeWorks()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new BlockingHandler(entered));
        await using var manager = new LocalModelManager(directory, http, _ => long.MaxValue, Definition);
        Directory.CreateDirectory(Path.GetDirectoryName(manager.ModelPath)!);
        await File.WriteAllBytesAsync(manager.TemporaryPath, Payload[..4]);
        var download = manager.StartAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(ModelDeleteResult.InUse, await manager.DeleteAsync());
        manager.Cancel();
        await download;
        Assert.Equal(LocalModelState.Paused, manager.Progress.State);
        Assert.Equal(Payload[..4], await File.ReadAllBytesAsync(manager.TemporaryPath));
    }

    [Fact]
    public async Task BodyIdleTimeoutFailsAndPreservesCheckpoint()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.PartialContent)
        {
            Content = new StreamContent(new StalledStream())
            { Headers = { ContentRange = new ContentRangeHeaderValue(4, Payload.Length - 1, Payload.Length) } }
        })) { Timeout = TimeSpan.FromMilliseconds(60) };
        await using var manager = new LocalModelManager(directory, http, _ => long.MaxValue, Definition);
        Directory.CreateDirectory(Path.GetDirectoryName(manager.TemporaryPath)!);
        await File.WriteAllBytesAsync(manager.TemporaryPath, Payload[..4]);
        var download = manager.StartAsync();
        var completed = await Task.WhenAny(download, Task.Delay(500));
        if (!download.IsCompleted) { manager.Cancel(); await download; }
        Assert.Same(download, completed);
        Assert.Equal(LocalModelState.Failed, manager.Progress.State);
        Assert.Equal(ModelFailureReason.Network, manager.Progress.FailureReason);
        Assert.Equal(Payload[..4], await File.ReadAllBytesAsync(manager.TemporaryPath));
    }

    [Fact]
    public async Task IgnoredRangeCreditsSpaceFreedByTruncatingCheckpoint()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) }));
        await using var manager = new LocalModelManager(directory, http, _ => 8 * 1024 * 1024 + Payload.Length - 4, Definition);
        Directory.CreateDirectory(Path.GetDirectoryName(manager.TemporaryPath)!);
        await File.WriteAllBytesAsync(manager.TemporaryPath, Payload[..4]);
        await manager.ResumeAsync();
        Assert.Equal(LocalModelState.Ready, manager.Progress.State);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(manager.ModelPath));
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request));
    }
    private sealed class BlockingHandler(TaskCompletionSource entered) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}

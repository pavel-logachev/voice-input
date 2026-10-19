using VoiceInput.Core.Audio;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests;

public sealed class LocalModelLifetimeTests
{
    [Fact]
    public async Task DeleteReleasesLoadedModelAndExcludesConcurrentDictation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lifetime-" + Guid.NewGuid().ToString("N"));
        await using var manager = new LocalModelManager(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(manager.ModelPath)!);
        await File.WriteAllBytesAsync(manager.ModelPath, [1]);
        LockedClient? current = null;
        await using var engine = new LocalEngineTranscriber(() =>
        {
            current = new LockedClient(manager.ModelPath);
            return (current, current);
        }, manager.ModelPath);
        try
        {
            await engine.TranscribeAsync(new RecordedAudio(new float[1600], 16000), CancellationToken.None);
            Assert.NotNull(current);
            Assert.False(current.Disposed);
            Assert.Equal(ModelDeleteResult.Deleted, await manager.DeleteAsync());
            Assert.True(current.Disposed);
            Assert.False(manager.IsInstalled);
            Assert.Equal(LocalModelState.NotInstalled, manager.Progress.State);
            await File.WriteAllBytesAsync(manager.ModelPath, [1]);
            await engine.TranscribeAsync(new RecordedAudio(new float[1600], 16000), CancellationToken.None);
            Assert.False(current.Disposed); // The selected engine can reload after re-installation.
        }
        finally
        {
            await engine.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task DeleteReturnsInUseWhileEngineOperationIsRunning()
    {
        var directory = Path.Combine(Path.GetTempPath(), "busy-" + Guid.NewGuid().ToString("N"));
        await using var manager = new LocalModelManager(directory);
        await using var engine = new LocalEngineTranscriber(() => throw new InvalidOperationException(), manager.ModelPath);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = engine.TryWithModelReleasedAsync(async () => { entered.SetResult(); await release.Task; });
        await entered.Task;
        try { Assert.Equal(ModelDeleteResult.InUse, await manager.DeleteAsync()); }
        finally { release.SetResult(); await operation; }
    }

    private sealed class LockedClient(string path) : IAsrSegmentClient, IAsyncDisposable
    {
        private readonly FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        public bool Disposed { get; private set; }
        public ValueTask<string> TranscribeSegmentAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken cancellationToken) => ValueTask.FromResult("ok");
        public ValueTask DisposeAsync() { Disposed = true; file.Dispose(); return ValueTask.CompletedTask; }
    }
}

using System.Collections.Concurrent;

namespace VoiceInput.Windows.Transcription;

public interface ILocalModelLifetime
{
    Task<bool> TryWithModelReleasedAsync(Func<Task> operation, CancellationToken cancellationToken = default);
}

internal static class LocalModelLifetimes
{
    private static readonly ConcurrentDictionary<string, WeakReference<ILocalModelLifetime>> Engines = new(StringComparer.OrdinalIgnoreCase);
    public static void Register(string path, ILocalModelLifetime engine) => Engines[Path.GetFullPath(path)] = new(engine);
    public static async Task<bool> TryReleaseAsync(string path, Func<Task> operation)
    {
        if (Engines.TryGetValue(Path.GetFullPath(path), out var reference) && reference.TryGetTarget(out var engine))
            return await engine.TryWithModelReleasedAsync(operation).ConfigureAwait(false);
        await operation().ConfigureAwait(false);
        return true;
    }
}

public enum ModelDeleteResult { Deleted, InUse, FileAccess }

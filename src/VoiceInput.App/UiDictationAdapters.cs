using System.Windows.Threading;
using VoiceInput.Core.Activation;

namespace VoiceInput.App;

// Core workflow does not capture a UI context. Only these UI-bound ports dispatch back to WPF.
internal sealed class UiActivationOverlay(Dispatcher dispatcher, IActivationOverlay overlay) : IActivationOverlay
{
    public void Show(ActivationVisualState state) => Post(() => overlay.Show(state));
    public void Hide() => Post(overlay.Hide);
    private void Post(Action action)
    {
        if (dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
}

internal sealed class UiTextInserter(Dispatcher dispatcher, ITextInserter inserter) : ITextInserter
{
    public async ValueTask InsertAsync(InputTarget target, string text, CancellationToken cancellationToken)
    {
        await dispatcher.InvokeAsync(() => inserter.InsertAsync(target, text, cancellationToken).AsTask(),
            DispatcherPriority.Normal, cancellationToken).Task.Unwrap().ConfigureAwait(false);
    }
}

internal static class BoundedShutdown
{
    public static async Task DisposeAsync(IAsyncDisposable resource, TimeSpan timeout, Action? forceStop = null)
    {
        var disposal = Task.Run(async () => await resource.DisposeAsync().ConfigureAwait(false));
        try { await disposal.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            forceStop?.Invoke();
            _ = disposal.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}

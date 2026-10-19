using System.Diagnostics;
using System.IO.Pipes;

namespace VoiceInput.Windows.Transcription;

/// <summary>Production binary protocol transport. Disposing always terminates only the process it started.</summary>
public sealed class NamedPipeWorkerConnection(string workerExecutable, string runtimeDirectory, string modelPath) : IAsrWorkerConnection, IForceStop
{
    private Process? process;
    private NamedPipeClientStream? pipe;
    public bool HasExited => process is null || process.HasExited;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(workerExecutable)) throw new FileNotFoundException("Не найден рабочий процесс локального движка.", workerExecutable);
        if (!File.Exists(Path.Combine(runtimeDirectory, "transcribe.dll"))) throw new FileNotFoundException("Не найден runtime локального движка.");
        if (!File.Exists(modelPath)) throw new TranscriptionException(TranscriptionErrorCode.ModelNotDownloaded, VoiceInputStrings.TranscriptionError(TranscriptionErrorCode.ModelNotDownloaded));
        var pipeName = $"VoiceInput.Asr.{Guid.NewGuid():N}";
        var start = new ProcessStartInfo { FileName = workerExecutable, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--pipe"); start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("--runtime"); start.ArgumentList.Add(runtimeDirectory);
        start.ArgumentList.Add("--model"); start.ArgumentList.Add(modelPath);
        process = Process.Start(start) ?? throw new IOException("ASR worker failed to start.");
        pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        process.Exited += (_, _) => { try { deadline.Cancel(); } catch (ObjectDisposedException) { } };
        process.EnableRaisingEvents = true;
        try { await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && process.HasExited)
        { throw new IOException("ASR worker exited during startup."); }
    }

    public async ValueTask<string> TranscribeAsync(ReadOnlyMemory<float> samples, int sampleRate, CancellationToken cancellationToken)
    {
        var active = pipe ?? throw new IOException("ASR pipe is not connected.");
        await AsrWorkerProtocol.WriteRequestAsync(active, samples, sampleRate, cancellationToken).ConfigureAwait(false);
        return await AsrWorkerProtocol.ReadResponseAsync(active, cancellationToken).ConfigureAwait(false);
    }

    public void ForceStop()
    {
        try { pipe?.Dispose(); } catch (IOException) { }
        try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        pipe?.Dispose();
        pipe = null;
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) { }
        finally { process.Dispose(); process = null; }
    }
}

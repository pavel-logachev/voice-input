using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace VoiceInput.Windows.Transcription;

public sealed class GigaAmWorkerClient : IAsrSegmentClient, IAsyncDisposable
{
    private const byte TranscribeCommand = 1;
    private const byte ShutdownCommand = 2;

    private readonly string workerExecutable;
    private readonly string runtimeDirectory;
    private readonly string modelPath;
    private readonly SemaphoreSlim exchangeLock = new(1, 1);
    private readonly ConcurrentQueue<string> workerLog = new();

    private Process? process;
    private NamedPipeClientStream? pipe;
    private BinaryReader? reader;
    private BinaryWriter? writer;
    private bool disposed;

    public GigaAmWorkerClient(string workerExecutable, string runtimeDirectory, string modelPath)
    {
        this.workerExecutable = Path.GetFullPath(workerExecutable);
        this.runtimeDirectory = Path.GetFullPath(runtimeDirectory);
        this.modelPath = Path.GetFullPath(modelPath);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (process is not null)
        {
            return;
        }

        if (!File.Exists(workerExecutable))
        {
            throw new FileNotFoundException("The Voice Input ASR worker was not found.", workerExecutable);
        }

        if (!Directory.Exists(runtimeDirectory))
        {
            throw new DirectoryNotFoundException($"The transcribe.cpp runtime directory was not found: {runtimeDirectory}");
        }

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The GigaAM model was not found.", modelPath);
        }

        var pipeName = $"VoiceInput.Asr.{Guid.NewGuid():N}";
        var startInfo = new ProcessStartInfo
        {
            FileName = workerExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--runtime");
        startInfo.ArgumentList.Add(runtimeDirectory);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(modelPath);

        var workerProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        workerProcess.ErrorDataReceived += OnWorkerLog;
        workerProcess.OutputDataReceived += OnWorkerLog;

        if (!workerProcess.Start())
        {
            workerProcess.Dispose();
            throw new InvalidOperationException("The Voice Input ASR worker did not start.");
        }

        workerProcess.BeginErrorReadLine();
        workerProcess.BeginOutputReadLine();

        var nextPipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await nextPipe.ConnectAsync(15_000, cancellationToken);
        }
        catch (Exception exception)
        {
            nextPipe.Dispose();
            if (!workerProcess.HasExited)
            {
                workerProcess.Kill(entireProcessTree: true);
            }

            await workerProcess.WaitForExitAsync(CancellationToken.None);
            workerProcess.Dispose();
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new InvalidOperationException(
                $"Could not connect to the ASR worker. {GetWorkerLog()}",
                exception);
        }

        process = workerProcess;
        pipe = nextPipe;
        reader = new BinaryReader(nextPipe, System.Text.Encoding.UTF8, leaveOpen: true);
        writer = new BinaryWriter(nextPipe, System.Text.Encoding.UTF8, leaveOpen: true);
    }

    public async ValueTask<string> TranscribeSegmentAsync(
        ReadOnlyMemory<float> samples,
        int sampleRate,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await exchangeLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Exchange(samples, sampleRate));
        }
        finally
        {
            exchangeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await exchangeLock.WaitAsync();
        try
        {
            if (pipe?.IsConnected == true && writer is not null)
            {
                try
                {
                    writer.Write(ShutdownCommand);
                    writer.Flush();
                }
                catch (IOException)
                {
                    // The worker has already gone away.
                }
            }

            reader?.Dispose();
            writer?.Dispose();
            pipe?.Dispose();

            if (process is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    await process.WaitForExitAsync(CancellationToken.None);
                }

                process.Dispose();
            }
        }
        finally
        {
            exchangeLock.Release();
            exchangeLock.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private string Exchange(ReadOnlyMemory<float> samples, int sampleRate)
    {
        var activePipe = pipe ?? throw new InvalidOperationException("The ASR worker has not been started.");
        var activeReader = reader ?? throw new InvalidOperationException("The ASR worker response reader is missing.");
        var activeWriter = writer ?? throw new InvalidOperationException("The ASR worker request writer is missing.");

        try
        {
            activeWriter.Write(TranscribeCommand);
            activeWriter.Write(sampleRate);
            activeWriter.Write(samples.Length);
            activeWriter.Write(MemoryMarshal.AsBytes(samples.Span));
            activeWriter.Flush();

            var status = activeReader.ReadByte();
            var message = activeReader.ReadString();
            if (status != 0)
            {
                throw new InvalidOperationException($"Local transcription failed: {message}");
            }

            return message;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidOperationException($"The ASR worker stopped unexpectedly. {GetWorkerLog()}", exception);
        }
        catch (IOException exception) when (!activePipe.IsConnected)
        {
            throw new InvalidOperationException($"The ASR worker connection was lost. {GetWorkerLog()}", exception);
        }
    }

    private void OnWorkerLog(object sender, DataReceivedEventArgs eventArgs)
    {
        if (eventArgs.Data is null)
        {
            return;
        }

        workerLog.Enqueue(eventArgs.Data);
        while (workerLog.Count > 20)
        {
            workerLog.TryDequeue(out _);
        }
    }

    private string GetWorkerLog() => string.Join(Environment.NewLine, workerLog);
}

using System.Runtime.InteropServices;

namespace VoiceInput.Asr.Worker;

internal sealed class GigaAmSession : IDisposable
{
    private const int CpuBackend = 1;
    private nint session;

    public GigaAmSession(string runtimeDirectory, string modelPath)
    {
        TranscribeNative.Configure(runtimeDirectory);
        ThrowOnError(TranscribeNative.InitBackendsDefault(), "initialize transcribe.cpp backends");

        var loadParameters = new TranscribeNative.ModelLoadParams();
        TranscribeNative.ModelLoadParamsInit(ref loadParameters);
        loadParameters.Backend = CpuBackend;

        ThrowOnError(
            TranscribeNative.Open(Path.GetFullPath(modelPath), ref loadParameters, nint.Zero, out session),
            "load the GigaAM model");

        var model = TranscribeNative.GetModel(session);
        Backend = Marshal.PtrToStringUTF8(TranscribeNative.ModelBackend(model)) ?? "unknown";
    }

    public string Backend { get; }

    public string Transcribe(float[] samples)
    {
        ObjectDisposedException.ThrowIf(session == nint.Zero, this);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfZero(samples.Length);

        ThrowOnError(TranscribeNative.Run(session, samples, samples.Length, nint.Zero), "transcribe audio");
        return Marshal.PtrToStringUTF8(TranscribeNative.FullText(session)) ?? string.Empty;
    }

    public void Dispose()
    {
        if (session == nint.Zero)
        {
            return;
        }

        TranscribeNative.SessionFree(session);
        session = nint.Zero;
        GC.SuppressFinalize(this);
    }

    private static void ThrowOnError(int status, string operation)
    {
        if (status == 0)
        {
            return;
        }

        var message = Marshal.PtrToStringUTF8(TranscribeNative.StatusString(status)) ?? $"status {status}";
        throw new InvalidOperationException($"Could not {operation}: {message}.");
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VoiceInput.Asr.Worker;

internal static class TranscribeNative
{
    private const string LibraryName = "transcribe";
    private static bool configured;
    private static nint libraryHandle;

    public static void Configure(string runtimeDirectory)
    {
        if (configured)
        {
            return;
        }

        var fullDirectory = Path.GetFullPath(runtimeDirectory);
        var libraryPath = Path.Combine(fullDirectory, "transcribe.dll");
        if (!File.Exists(libraryPath))
        {
            throw new FileNotFoundException("transcribe.dll was not found.", libraryPath);
        }

        if (!SetDllDirectory(fullDirectory))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not configure the native DLL search directory.");
        }

        libraryHandle = NativeLibrary.Load(libraryPath);
        NativeLibrary.SetDllImportResolver(
            typeof(TranscribeNative).Assembly,
            (name, _, _) => string.Equals(name, LibraryName, StringComparison.Ordinal) ? libraryHandle : nint.Zero);
        configured = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ModelLoadParams
    {
        public ulong StructSize;
        public int Backend;
        public int GpuDevice;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "transcribe_init_backends_default")]
    internal static extern int InitBackendsDefault();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "transcribe_model_load_params_init")]
    internal static extern void ModelLoadParamsInit(ref ModelLoadParams parameters);

    [DllImport(
        LibraryName,
        CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "transcribe_open",
        CharSet = CharSet.Ansi,
        BestFitMapping = false,
        ThrowOnUnmappableChar = true)]
    internal static extern int Open(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath,
        ref ModelLoadParams loadParameters,
        nint sessionParameters,
        out nint session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "transcribe_run")]
    internal static extern int Run(nint session, [In] float[] pcm, int sampleCount, nint runParameters);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "transcribe_full_text")]
    internal static extern nint FullText(nint session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "transcribe_get_model")]
    internal static extern nint GetModel(nint session);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "transcribe_model_backend")]
    internal static extern nint ModelBackend(nint model);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "transcribe_status_string")]
    internal static extern nint StatusString(int status);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "transcribe_session_free")]
    internal static extern void SessionFree(nint session);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string pathName);
}

using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using VoiceInput.Windows.Settings;

namespace VoiceInput.App;

/// <summary>Local, bounded crash metadata. Never attempts to recover from an unhandled exception.</summary>
internal static class CrashLog
{
    private const int MaximumBytes = 64 * 1024;
    private static readonly object Gate = new();
    private static readonly string Version = typeof(CrashLog).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    internal static void Register(System.Windows.Application application)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Append("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        application.DispatcherUnhandledException += (_, args) =>
            Append("DispatcherUnhandledException", args.Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            Append("TaskScheduler.UnobservedTaskException", args.Exception);
    }

    internal static void Append(string source, Exception? exception)
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:O} version={Version} source={source}");
            AppendException(text, exception, 0);
            var entry = text.ToString();
            if (Encoding.UTF8.GetByteCount(entry) > MaximumBytes / 2)
                entry = entry[..Math.Min(entry.Length, MaximumBytes / 8)] + "\n[truncated]\n";
            lock (Gate)
            {
                var directory = Path.Combine(AppDataPaths.Root, "logs");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "crash.log");
                if (File.Exists(path) && new FileInfo(path).Length + Encoding.UTF8.GetByteCount(entry) > MaximumBytes)
                    File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, entry, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logging must not replace the original failure or perform UI/network work.
        }
    }

    private static void AppendException(StringBuilder text, Exception? exception, int depth)
    {
        if (exception is null || depth >= 8) return;
        text.AppendLine(CultureInfo.InvariantCulture, $"type={exception.GetType().FullName} hresult=0x{exception.HResult:X8}");
        // Exception.Message is arbitrary input: HTTP errors and third-party code may embed
        // transcript, clipboard contents or credentials. Never persist that payload.
        text.AppendLine("message=[redacted: untrusted exception message]");
        // Build from runtime frames, not a potentially overridden Exception.StackTrace property.
        var stack = new System.Diagnostics.StackTrace(exception, fNeedFileInfo: false).ToString();
        text.AppendLine(string.IsNullOrEmpty(stack) ? "[no managed stack]" : stack);
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions.Take(8)) AppendException(text, inner, depth + 1);
        }
        else AppendException(text, exception.InnerException, depth + 1);
    }
}

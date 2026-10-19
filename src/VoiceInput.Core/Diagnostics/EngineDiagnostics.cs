using System.Globalization;

namespace VoiceInput.Core.Diagnostics;

/// <summary>Opt-in phase timings only: never accepts audio, transcripts, URLs or credentials.</summary>
public static class EngineDiagnostics
{
    private static readonly object Gate = new();
    public static void WriteTiming(string phase, TimeSpan elapsed)
    {
        var path = Environment.GetEnvironmentVariable("VOICE_INPUT_DIAGNOSTIC_LOG");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            lock (Gate)
            {
                File.AppendAllText(path, string.Create(CultureInfo.InvariantCulture,
                    $"{DateTimeOffset.UtcNow:O} phase={phase} elapsed_ms={elapsed.TotalMilliseconds:F1}{Environment.NewLine}"));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { /* Diagnostics must never affect dictation. */ }
    }
}

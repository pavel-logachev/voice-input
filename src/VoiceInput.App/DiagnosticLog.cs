using System.IO;

namespace VoiceInput.App;

/// <summary>Optional plain-text trace, enabled by the VOICE_INPUT_DIAGNOSTIC_LOG environment variable. Never throws.</summary>
internal static class DiagnosticLog
{
    public static void TryAppend(string? path, string message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Diagnostics must never affect dictation.
        }
    }
}

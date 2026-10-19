using VoiceInput.App;

namespace VoiceInput.Windows.Tests;

public sealed class CrashLogTests
{
    [Fact]
    public void CrashLogIsBoundedContainsMetadataAndNeverPersistsExceptionPayload()
    {
        // AppDataPaths reads this process-wide override; this collection never changes user data.
        var previous = Environment.GetEnvironmentVariable("VOICE_INPUT_DATA_DIR");
        var directory = Path.Combine(Path.GetTempPath(), "crash-test", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("VOICE_INPUT_DATA_DIR", directory);
            Exception failure;
            try { throw new InvalidOperationException("SECRET transcript clipboard API-key"); }
            catch (InvalidOperationException exception) { failure = exception; }
            for (var i = 0; i < 500; i++) CrashLog.Append("test", failure);
            var path = Path.Combine(directory, "logs", "crash.log");
            var text = File.ReadAllText(path);
            Assert.Contains("version=", text, StringComparison.Ordinal);
            Assert.Contains("System.InvalidOperationException", text, StringComparison.Ordinal);
            Assert.Contains(nameof(CrashLogIsBoundedContainsMetadataAndNeverPersistsExceptionPayload), text, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET", text, StringComparison.Ordinal);
            Assert.True(new FileInfo(path).Length <= 65536);
            Assert.True(new FileInfo(path + ".1").Length <= 65536);
        }
        finally { Environment.SetEnvironmentVariable("VOICE_INPUT_DATA_DIR", previous); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

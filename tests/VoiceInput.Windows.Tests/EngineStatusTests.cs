using VoiceInput.Windows.Settings;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests;

public sealed class EngineStatusTests
{
    [Fact]
    public void MissingModelAndUnconfiguredServerHaveDistinctApprovedStatuses()
    {
        var local = EngineStatus.Evaluate(new AppSettings { Engine = TranscriptionEngine.Local }, false, true);
        var custom = EngineStatus.Evaluate(new AppSettings { Engine = TranscriptionEngine.CustomOpenAiCompatible }, true, true);
        Assert.Equal(EngineReadiness.LocalModelNotInstalled, local);
        Assert.Equal(EngineReadiness.CustomNotConfigured, custom);
        Assert.Equal("Модель не загружена", EngineStrings.Status(local, TranscriptionEngine.Local));
        Assert.Equal("Свой сервер не настроен", EngineStrings.Status(custom, TranscriptionEngine.CustomOpenAiCompatible));
    }
}

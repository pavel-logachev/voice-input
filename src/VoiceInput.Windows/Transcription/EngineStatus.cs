using VoiceInput.Windows.Settings;

namespace VoiceInput.Windows.Transcription;

public enum EngineReadiness { Ready, LocalModelNotInstalled, CustomNotConfigured, OpenAiKeyMissing }

/// <summary>Temporary product strings grouped for the writer. No endpoint or credential is interpolated.</summary>
public static class EngineStrings
{
    public static string LocalModelNotInstalled => VoiceInputStrings.Get("tray.status.localModelMissing");
    public static string CustomNotConfigured => VoiceInputStrings.Get("tray.status.customNotConfigured");
    public static string OpenAiKeyMissing => VoiceInputStrings.Get("tray.status.openAiKeyMissing");
    public static string LocalReady => VoiceInputStrings.Get("tray.status.localReady");
    public static string CustomReady => VoiceInputStrings.Get("tray.status.customReady");
    public static string OpenAiReady => VoiceInputStrings.Get("tray.status.openAiReady");
    public static string OpenAiMenu => VoiceInputStrings.Get("tray.engine.openai");
    public static string LocalMenu => VoiceInputStrings.Get("tray.engine.local");
    public static string CustomMenu => VoiceInputStrings.Get("tray.engine.custom");

    public static string Status(EngineReadiness state, TranscriptionEngine engine) => state switch
    {
        EngineReadiness.LocalModelNotInstalled => LocalModelNotInstalled,
        EngineReadiness.CustomNotConfigured => CustomNotConfigured,
        EngineReadiness.OpenAiKeyMissing => OpenAiKeyMissing,
        _ => engine switch
        {
            TranscriptionEngine.Local => LocalReady,
            TranscriptionEngine.CustomOpenAiCompatible => CustomReady,
            _ => OpenAiReady,
        },
    };
}

public static class EngineStatus
{
    public static EngineReadiness Evaluate(AppSettings settings, bool localModelInstalled, bool openAiHasKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Engine == TranscriptionEngine.Local && !localModelInstalled) return EngineReadiness.LocalModelNotInstalled;
        if (settings.Engine == TranscriptionEngine.OpenAi && !openAiHasKey) return EngineReadiness.OpenAiKeyMissing;
        if (settings.Engine == TranscriptionEngine.CustomOpenAiCompatible)
        {
            if (string.IsNullOrWhiteSpace(settings.CustomModel)) return EngineReadiness.CustomNotConfigured;
            try { CustomEndpointPolicy.NormalizeBaseUrl(settings.CustomBaseUrl ?? ""); }
            catch (ArgumentException) { return EngineReadiness.CustomNotConfigured; }
        }
        return EngineReadiness.Ready;
    }
}

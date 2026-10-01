namespace VoiceInput.Windows.Transcription;

public interface IOpenAiApiKeyProvider
{
    /// <summary>Returns the stored API key, or throws <see cref="InvalidOperationException"/> with a user-facing message.</summary>
    string GetApiKey();
}

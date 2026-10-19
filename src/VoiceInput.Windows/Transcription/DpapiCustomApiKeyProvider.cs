using System.Security.Cryptography;
using System.Text;
using VoiceInput.Windows.Settings;

namespace VoiceInput.Windows.Transcription;

/// <summary>Independent optional credential for the user's OpenAI-compatible server.</summary>
public sealed class DpapiCustomApiKeyProvider(string? credentialPath = null) : IOpenAiApiKeyProvider
{
    private static readonly byte[] Entropy = "VoiceInput.CustomOpenAI.v1"u8.ToArray();
    public static string DefaultCredentialPath => Path.Combine(AppDataPaths.Root, "secrets", "custom-api-key.dpapi");
    public string FilePath { get; } = credentialPath ?? DefaultCredentialPath;
    public bool HasKey => File.Exists(FilePath);

    public static bool IsValidKey(string? apiKey) => apiKey is null || !apiKey.Any(char.IsControl);

    public void Save(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        if (!IsValidKey(apiKey)) throw new ArgumentException(VoiceInputStrings.Get("custom.key.error.invalidChars"));
        var key = apiKey.Trim();
        if (key.Length == 0) { Delete(); return; }
        var plain = Encoding.UTF8.GetBytes(key);
        try
        {
            var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllBytes(FilePath + ".tmp", encrypted);
            File.Move(FilePath + ".tmp", FilePath, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void Delete() => File.Delete(FilePath);

    public string GetApiKey()
    {
        if (!HasKey) return string.Empty;
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception exception) when (exception is IOException or CryptographicException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(VoiceInputStrings.Get("custom.key.error.readFailed"));
        }
    }
}

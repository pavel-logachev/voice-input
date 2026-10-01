using System.Security.Cryptography;
using System.Text;

namespace VoiceInput.Windows.Transcription;

/// <summary>
/// Stores the OpenAI API key encrypted with DPAPI for the current Windows user. The file is readable
/// only by this user on this machine and never leaves it except as the request's bearer token.
/// </summary>
public sealed class DpapiOpenAiApiKeyProvider : IOpenAiApiKeyProvider
{
    private const int MinimumKeyLength = 40;
    private static readonly byte[] OptionalEntropy = "VoiceInput.OpenAI.v1"u8.ToArray();

    private readonly string credentialPath;

    public DpapiOpenAiApiKeyProvider(string? credentialPath = null)
    {
        this.credentialPath = credentialPath ?? DefaultCredentialPath;
    }

    public static string DefaultCredentialPath =>
        Path.Combine(Windows.Settings.AppDataPaths.Root, "secrets", "openai-api-key.dpapi");

    public bool HasKey => File.Exists(credentialPath);

    public static bool LooksLikeApiKey(string? value) =>
        value is not null
        && value.Length >= MinimumKeyLength
        && value.StartsWith("sk-", StringComparison.Ordinal)
        && value.All(static c => c is > ' ' and < '\u007f');

    public void Save(string apiKey)
    {
        var trimmed = apiKey?.Trim();
        if (!LooksLikeApiKey(trimmed))
        {
            throw new ArgumentException("Это не похоже на API-ключ OpenAI: он начинается с «sk-».", nameof(apiKey));
        }

        var plain = Encoding.UTF8.GetBytes(trimmed!);
        try
        {
            var protectedBytes = ProtectedData.Protect(plain, OptionalEntropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(credentialPath)!);
            var temporary = credentialPath + ".tmp";
            File.WriteAllBytes(temporary, protectedBytes);
            File.Move(temporary, credentialPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Delete()
    {
        if (File.Exists(credentialPath))
        {
            File.Delete(credentialPath);
        }
    }

    public string GetApiKey()
    {
        if (!File.Exists(credentialPath))
        {
            throw new InvalidOperationException("API-ключ OpenAI не настроен.");
        }

        byte[] plain;
        try
        {
            plain = ProtectedData.Unprotect(File.ReadAllBytes(credentialPath), OptionalEntropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Не удалось прочитать защищённый API-ключ OpenAI.");
        }
        catch (IOException)
        {
            throw new InvalidOperationException("Не удалось открыть защищённый API-ключ OpenAI.");
        }

        try
        {
            var text = Encoding.UTF8.GetString(plain).Trim();
            if (!LooksLikeApiKey(text))
            {
                throw new InvalidOperationException("Защищённый API-ключ OpenAI повреждён.");
            }

            return text;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoiceInput.Windows.Settings;

public enum TranscriptionEngine
{
    /// <summary>Audio is sent to the OpenAI transcription API with the user's own API key.</summary>
    OpenAi,

    /// <summary>Recognition runs on this computer (GigaAM); audio never leaves it.</summary>
    Local,
}

public sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TranscriptionEngine Engine { get; init; } = TranscriptionEngine.OpenAi;

    public bool StartWithWindows { get; init; }

    /// <summary>Optional ISO-639-1 hint for the cloud engine (for example "ru"); empty means automatic.</summary>
    public string? Language { get; init; }

    /// <summary>Windows endpoint id of the chosen microphone; empty means the Windows default.</summary>
    public string? MicrophoneId { get; init; }
}

/// <summary>Reads and writes settings as JSON. A missing or damaged file never stops the application.</summary>
public sealed class SettingsStore(string? path = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object gate = new();

    public static string DefaultPath => System.IO.Path.Combine(AppDataPaths.Root, "settings.json");

    public string FilePath { get; } = path ?? DefaultPath;

    public AppSettings Load()
    {
        lock (gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return new AppSettings();
                }

                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                return Normalize(settings);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (gate)
        {
            var directory = System.IO.Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Normalize(settings), Options));
            File.Move(temporary, FilePath, overwrite: true);
        }
    }

    private static AppSettings Normalize(AppSettings? settings)
    {
        if (settings is null)
        {
            return new AppSettings();
        }

        var language = string.IsNullOrWhiteSpace(settings.Language) ? null : settings.Language.Trim().ToLowerInvariant();
        return settings with
        {
            Version = AppSettings.CurrentVersion,
            Engine = Enum.IsDefined(settings.Engine) ? settings.Engine : TranscriptionEngine.OpenAi,
            Language = language is { Length: 2 or 3 } ? language : null,
            MicrophoneId = string.IsNullOrWhiteSpace(settings.MicrophoneId) ? null : settings.MicrophoneId.Trim(),
        };
    }
}

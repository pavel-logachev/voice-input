using System.Globalization;
using VoiceInput.Windows.Settings;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.App;

/// <summary>How the engine section speaks: which approved string a state shows and how its numbers are written.</summary>
internal static class EngineCopy
{
    private const double BytesPerMegabyte = 1_000_000;

    /// <summary>The margin the model download keeps free on the disk, on top of the file itself.</summary>
    private const long DiskReserveBytes = 8 * 1024 * 1024;

    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    internal enum Tone
    {
        Neutral,
        Good,
        Warn,
        Bad,
    }

    public static string Text(string key) => VoiceInputStrings.Get(key);

    public static string LocalState(LocalModelProgress progress, long modelSize)
    {
        var total = progress.TotalBytes is > 0 ? progress.TotalBytes.Value : modelSize;
        return progress.State switch
        {
            LocalModelState.Downloading => Text("local.state.downloading")
                .Replace("{downloaded}", Downloaded(progress.Bytes, total), StringComparison.Ordinal)
                .Replace("{total}", Megabytes(total), StringComparison.Ordinal)
                .Replace("{speed}", Speed(progress.BytesPerSecond), StringComparison.Ordinal),
            LocalModelState.Paused => Text("local.state.paused")
                .Replace("{downloaded}", Downloaded(progress.Bytes, total), StringComparison.Ordinal)
                .Replace("{total}", Megabytes(total), StringComparison.Ordinal),
            LocalModelState.Verifying => Text("local.state.verifying"),
            LocalModelState.Ready => Text("local.state.ready"),
            LocalModelState.Failed => progress.FailureReason switch
            {
                ModelFailureReason.InsufficientDiskSpace => Text("local.state.failed.diskSpace")
                    .Replace("{required}", Required(progress.Bytes, total), StringComparison.Ordinal),
                ModelFailureReason.HashMismatch => Text("local.state.failed.hashMismatch"),
                ModelFailureReason.FileAccess => Text("local.state.failed.fileAccess"),
                ModelFailureReason.InvalidResponse => Text("local.state.failed.invalidResponse"),
                _ => Text("local.state.failed.network"),
            },
            _ => Text("local.state.notInstalled"),
        };
    }

    public static Tone LocalTone(LocalModelState state) => state switch
    {
        LocalModelState.Ready => Tone.Good,
        LocalModelState.NotInstalled => Tone.Warn,
        LocalModelState.Failed => Tone.Bad,
        _ => Tone.Neutral,
    };

    /// <summary>The one action a state offers; null while the file is being checked.</summary>
    public static string? LocalActionKey(LocalModelState state) => state switch
    {
        LocalModelState.NotInstalled => "local.action.download",
        LocalModelState.Downloading => "local.action.pause",
        LocalModelState.Paused => "local.action.resume",
        LocalModelState.Failed => "local.action.retry",
        LocalModelState.Ready => "local.action.delete",
        _ => null,
    };

    /// <summary>Moving forward is the primary action; pausing and deleting are not.</summary>
    public static bool LocalActionIsPrimary(LocalModelState state) =>
        state is LocalModelState.NotInstalled or LocalModelState.Paused or LocalModelState.Failed;

    /// <summary>Decimal megabytes, rounded: 183 948 704 bytes are 184 MB.</summary>
    public static string Megabytes(long bytes) =>
        Math.Round(bytes / BytesPerMegabyte, MidpointRounding.AwayFromZero).ToString("0", Russian);

    // The received part never reads as complete before the last byte has arrived.
    private static string Downloaded(long bytes, long total)
    {
        var value = Math.Floor(bytes / BytesPerMegabyte);
        return (bytes < total ? value : Math.Round(total / BytesPerMegabyte, MidpointRounding.AwayFromZero)).ToString("0", Russian);
    }

    private static string Speed(double bytesPerSecond) =>
        (Math.Max(0, bytesPerSecond) / BytesPerMegabyte).ToString("0.0", Russian);

    // What must be free on the disk: the rest of the file and the reserve the download keeps.
    private static string Required(long bytes, long total)
    {
        var needed = Math.Max(0, total - bytes) + DiskReserveBytes;
        return Math.Ceiling(needed / BytesPerMegabyte).ToString("0", Russian) + "\u00A0МБ";
    }

    /// <summary>The key of the error under the address field, or null when the address can be used.</summary>
    public static string? UrlErrorKey(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return "custom.url.error.required";
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return "custom.url.error.invalid";
        }

        return CustomEndpointPolicy.IsAllowed(uri) ? null : "custom.url.error.httpNotAllowed";
    }

    public static string? ModelErrorKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "custom.model.error.required" : null;

    public static bool IsCustomComplete(AppSettings settings) =>
        UrlErrorKey(settings.CustomBaseUrl) is null && ModelErrorKey(settings.CustomModel) is null;

    public static string CheckResult(CustomProviderValidationResult result) => Text(result.Status switch
    {
        CustomProviderValidationStatus.ModelVerified => "custom.check.modelVerified",
        CustomProviderValidationStatus.ReachableModelNotVerified => "custom.check.reachableModelNotVerified",
        _ => result.ErrorCode switch
        {
            TranscriptionErrorCode.InvalidKeyCharacters => "custom.key.error.invalidChars",
            TranscriptionErrorCode.Unauthorized => "custom.check.failed.unauthorized",
            TranscriptionErrorCode.Network => "custom.check.failed.network",
            TranscriptionErrorCode.Timeout => "custom.check.failed.timeout",
            TranscriptionErrorCode.ServiceUnavailable => "custom.check.failed.serviceUnavailable",
            TranscriptionErrorCode.RateLimited => "overlay.error.custom.rateLimited",
            TranscriptionErrorCode.InvalidRequest => "overlay.error.custom.invalidRequest",
            _ => "custom.check.failed.invalidResponse",
        },
    });

    public static Tone CheckTone(CustomProviderValidationStatus status) => status switch
    {
        CustomProviderValidationStatus.ModelVerified => Tone.Good,
        CustomProviderValidationStatus.ReachableModelNotVerified => Tone.Warn,
        _ => Tone.Bad,
    };
}

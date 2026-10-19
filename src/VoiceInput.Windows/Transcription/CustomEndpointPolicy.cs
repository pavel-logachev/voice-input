using System.Net;
using System.Net.Sockets;

namespace VoiceInput.Windows.Transcription;

/// <summary>HTTP is allowed only for literal private addresses and localhost, never arbitrary DNS names.</summary>
public static class CustomEndpointPolicy
{
    public static Uri NormalizeBaseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || !IsAllowed(uri))
        {
            throw new ArgumentException("Нужен HTTPS URL сервера; HTTP допустим только для localhost и частных IP-адресов.", nameof(value));
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public static bool IsAllowed(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        if (uri.Scheme != Uri.UriSchemeHttp) return false;
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168)
            : address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xfe) == 0xfc;
    }
}

public enum TranscriptionErrorCode
{
    ModelNotDownloaded, WorkerExited, WorkerTimeout, WorkerRestartLimit, WorkerProtocol,
    Unauthorized, RateLimited, InvalidRequest, ServiceUnavailable, Network, Timeout, InvalidResponse, CustomNotConfigured, InvalidKeyCharacters,
}

public sealed class TranscriptionException(TranscriptionErrorCode code, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public TranscriptionErrorCode Code { get; } = code;
    public bool OpenSettings => Code == TranscriptionErrorCode.ModelNotDownloaded;
}

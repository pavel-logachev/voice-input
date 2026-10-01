using System.Net;
using System.Net.Http.Headers;

namespace VoiceInput.Windows.Transcription;

public enum OpenAiKeyCheckStatus
{
    /// <summary>OpenAI accepted the key.</summary>
    Accepted,

    /// <summary>OpenAI rejected the key (wrong, revoked or from another organization).</summary>
    Rejected,

    /// <summary>The key is valid but restricted, so the check could not confirm audio access.</summary>
    Restricted,

    /// <summary>The check could not be completed (network, rate limit, service error).</summary>
    Inconclusive,
}

public sealed record OpenAiKeyCheckResult(OpenAiKeyCheckStatus Status, string Message);

/// <summary>Asks OpenAI whether a key works, using a read-only request that does not bill anything.</summary>
public static class OpenAiKeyCheck
{
    public static readonly Uri DefaultEndpoint = new("https://api.openai.com/v1/models");

    public static async Task<OpenAiKeyCheckResult> CheckAsync(
        HttpClient httpClient,
        string apiKey,
        CancellationToken cancellationToken,
        Uri? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (!DpapiOpenAiApiKeyProvider.LooksLikeApiKey(apiKey?.Trim()))
        {
            return new OpenAiKeyCheckResult(OpenAiKeyCheckStatus.Rejected, "Это не похоже на ключ OpenAI: он начинается с «sk-».");
        }

        var target = endpoint ?? DefaultEndpoint;
        if (!string.Equals(target.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The check endpoint must use HTTPS.", nameof(endpoint));
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey!.Trim());
        try
        {
            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => new OpenAiKeyCheckResult(OpenAiKeyCheckStatus.Accepted, "Ключ работает."),
                HttpStatusCode.Unauthorized => new OpenAiKeyCheckResult(
                    OpenAiKeyCheckStatus.Rejected,
                    "OpenAI отклонил ключ (HTTP 401). Проверьте, что он скопирован целиком и не отозван."),
                HttpStatusCode.Forbidden => new OpenAiKeyCheckResult(
                    OpenAiKeyCheckStatus.Restricted,
                    "Ключ принят, но у него ограниченные права, и проверка не смогла подтвердить доступ к распознаванию. Диктовка может работать."),
                HttpStatusCode.TooManyRequests => new OpenAiKeyCheckResult(
                    OpenAiKeyCheckStatus.Inconclusive,
                    "OpenAI ограничил частоту запросов (HTTP 429). Повторите проверку позже."),
                _ => new OpenAiKeyCheckResult(
                    OpenAiKeyCheckStatus.Inconclusive,
                    $"Проверка не завершилась: OpenAI ответил HTTP {(int)response.StatusCode}."),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OpenAiKeyCheckResult(OpenAiKeyCheckStatus.Inconclusive, "OpenAI не ответил вовремя. Проверьте подключение к интернету.");
        }
        catch (HttpRequestException)
        {
            return new OpenAiKeyCheckResult(OpenAiKeyCheckStatus.Inconclusive, "Не удалось связаться с OpenAI. Проверьте подключение к интернету.");
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace VoiceInput.Windows.Transcription;

public enum CustomProviderValidationStatus { ModelVerified, ReachableModelNotVerified, Failed }
public sealed record CustomProviderValidationResult(CustomProviderValidationStatus Status, TranscriptionErrorCode? ErrorCode = null);

public static class CustomProviderValidation
{
    public static async Task<CustomProviderValidationResult> ValidateAsync(
        HttpClient httpClient, string baseUrl, string model, string? apiKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (!DpapiCustomApiKeyProvider.IsValidKey(apiKey))
            return new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.InvalidKeyCharacters);
        var endpoint = new Uri(CustomEndpointPolicy.NormalizeBaseUrl(baseUrl), "models");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
                return new(CustomProviderValidationStatus.ReachableModelNotVerified);
            if (!response.IsSuccessStatusCode)
                return new(CustomProviderValidationStatus.Failed, response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => TranscriptionErrorCode.Unauthorized,
                    HttpStatusCode.TooManyRequests => TranscriptionErrorCode.RateLimited,
                    >= HttpStatusCode.InternalServerError => TranscriptionErrorCode.ServiceUnavailable,
                    _ => TranscriptionErrorCode.InvalidRequest,
                });
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: deadline.Token).ConfigureAwait(false);
            var found = json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                && data.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == model);
            return new(found ? CustomProviderValidationStatus.ModelVerified : CustomProviderValidationStatus.ReachableModelNotVerified);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.Timeout); }
        catch (HttpRequestException) { return new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.Network); }
        catch (IOException) { return new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.InvalidResponse); }
        catch (JsonException) { return new(CustomProviderValidationStatus.ReachableModelNotVerified); }
    }
}

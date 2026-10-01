using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VoiceInput.Core.Audio;
using VoiceInput.Core.Transcription;

namespace VoiceInput.Windows.Transcription;

/// <summary>Cloud transcription through the OpenAI audio API. Audio leaves the machine only for this request.</summary>
public sealed class OpenAiTranscriber : ITranscriber
{
    public const string DefaultModel = "gpt-transcribe";
    public const int MinimumAudioMilliseconds = 100;
    public const int MaximumUploadBytes = 25 * 1024 * 1024;

    private const int WaveHeaderSize = 44;

    public static readonly Uri DefaultEndpoint = new("https://api.openai.com/v1/audio/transcriptions");

    private readonly HttpClient httpClient;
    private readonly IOpenAiApiKeyProvider apiKeyProvider;
    private readonly Uri endpoint;
    private readonly TimeSpan requestTimeout;
    private readonly string model;
    private readonly string? language;

    public OpenAiTranscriber(
        HttpClient httpClient,
        IOpenAiApiKeyProvider apiKeyProvider,
        Uri? endpoint = null,
        TimeSpan? requestTimeout = null,
        string model = DefaultModel,
        string? language = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.apiKeyProvider = apiKeyProvider ?? throw new ArgumentNullException(nameof(apiKeyProvider));
        this.endpoint = endpoint ?? DefaultEndpoint;
        if (!string.Equals(this.endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The OpenAI transcription endpoint must use HTTPS.", nameof(endpoint));
        }

        this.requestTimeout = requestTimeout ?? RecommendedRequestTimeout;
        if (this.requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "The request timeout must be positive.");
        }

        this.model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        this.language = string.IsNullOrWhiteSpace(language) ? null : language;
    }

    public static TimeSpan RecommendedRequestTimeout { get; } = TimeSpan.FromMinutes(3);

    public async ValueTask<string> TranscribeAsync(RecordedAudio audio, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audio.SampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(audio), "Audio sample rate must be positive.");
        }

        var minimumSamples = Math.Max(1, audio.SampleRate * MinimumAudioMilliseconds / 1000);
        if (audio.Samples.Length < minimumSamples)
        {
            return string.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var wave = await Task.Run(() => EncodePcm16Wave(audio), cancellationToken).ConfigureAwait(false);
        var apiKey = apiKeyProvider.GetApiKey();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(requestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var multipart = new MultipartFormDataContent
        {
            { new StringContent(model, Encoding.UTF8), "model" },
            { new StringContent("json", Encoding.UTF8), "response_format" },
        };
        if (language is not null)
        {
            multipart.Add(new StringContent(language, Encoding.UTF8), "language");
        }

        using var audioContent = new ByteArrayContent(wave);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        multipart.Add(audioContent, "file", "dictation.wav");
        request.Content = multipart;

        try
        {
            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateRequestFailure(response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: deadline.Token).ConfigureAwait(false);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("OpenAI вернул ответ без расшифровки.");
            }

            return (text.GetString() ?? string.Empty).Trim();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("OpenAI не ответил вовремя.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("OpenAI вернул повреждённый ответ.");
        }
        catch (IOException)
        {
            throw new InvalidOperationException("Не удалось прочитать ответ OpenAI.");
        }
        catch (HttpRequestException exception) when (exception.InnerException is IOException)
        {
            throw new InvalidOperationException("Не удалось прочитать ответ OpenAI.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Не удалось связаться с OpenAI. Проверьте подключение к интернету.");
        }
    }

    internal static byte[] EncodePcm16Wave(RecordedAudio audio)
    {
        var dataSize = checked(audio.Samples.Length * sizeof(short));
        if (dataSize > MaximumUploadBytes - WaveHeaderSize)
        {
            throw new InvalidOperationException("Запись слишком длинная для отправки в OpenAI.");
        }

        using var memory = new MemoryStream(WaveHeaderSize + dataSize);
        using (var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataSize);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(audio.SampleRate);
            writer.Write(audio.SampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataSize);
            foreach (var sample in audio.Samples)
            {
                var scaled = (int)MathF.Round(Math.Clamp(sample, -1f, 1f) * 32767f);
                writer.Write((short)Math.Clamp(scaled, short.MinValue, short.MaxValue));
            }
        }

        return memory.ToArray();
    }

    private static InvalidOperationException CreateRequestFailure(HttpStatusCode statusCode) =>
        new(statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"OpenAI отклонил API-ключ (HTTP {(int)statusCode}).",
            HttpStatusCode.TooManyRequests =>
                $"Лимит или баланс OpenAI исчерпан (HTTP {(int)statusCode}).",
            < HttpStatusCode.InternalServerError =>
                $"OpenAI не выполнил расшифровку (HTTP {(int)statusCode}).",
            _ => $"Сервис OpenAI временно недоступен (HTTP {(int)statusCode}).",
        });
}

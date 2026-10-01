using System.Net;
using System.Text;
using VoiceInput.Core.Audio;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests.Transcription;

public sealed class OpenAiTranscriberTests
{
    private const string ApiKey = "sk-test-0123456789abcdef0123456789abcdef0123";

    [Fact]
    public async Task SendsWaveWithBearerKeyAndReturnsTrimmedText()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"text\":\"  Привет, мир.  \"}");
        var transcriber = Create(handler, language: "ru");

        var text = await transcriber.TranscribeAsync(Speech(), CancellationToken.None);

        Assert.Equal("Привет, мир.", text);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(OpenAiTranscriber.DefaultEndpoint, request.Uri);
        Assert.Equal($"Bearer {ApiKey}", request.Authorization);
        Assert.Contains("name=model", request.Body, StringComparison.Ordinal);
        Assert.Contains(OpenAiTranscriber.DefaultModel, request.Body, StringComparison.Ordinal);
        Assert.Contains("name=language", request.Body, StringComparison.Ordinal);
        Assert.Contains("filename=dictation.wav", request.Body, StringComparison.Ordinal);
        Assert.Contains("RIFF", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AudioShorterThanTheMinimumIsNotSent()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{\"text\":\"x\"}");
        var transcriber = Create(handler);
        var tooShort = new RecordedAudio(new float[1_000], 16_000);

        var text = await transcriber.TranscribeAsync(tooShort, CancellationToken.None);

        Assert.Equal(string.Empty, text);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "отклонил API-ключ")]
    [InlineData(HttpStatusCode.Forbidden, "отклонил API-ключ")]
    [InlineData(HttpStatusCode.TooManyRequests, "Лимит или баланс")]
    [InlineData(HttpStatusCode.BadRequest, "не выполнил расшифровку")]
    [InlineData(HttpStatusCode.BadGateway, "временно недоступен")]
    public async Task HttpFailuresBecomeSpecificUserMessages(HttpStatusCode status, string expected)
    {
        var transcriber = Create(new RecordingHandler(status, "{}"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transcriber.TranscribeAsync(Speech(), CancellationToken.None).AsTask());

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseWithoutTextIsRejected()
    {
        var transcriber = Create(new RecordingHandler(HttpStatusCode.OK, "{\"other\":1}"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transcriber.TranscribeAsync(Speech(), CancellationToken.None).AsTask());

        Assert.Contains("без расшифровки", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedJsonIsReportedAsDamagedResponse()
    {
        var transcriber = Create(new RecordingHandler(HttpStatusCode.OK, "not json"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transcriber.TranscribeAsync(Speech(), CancellationToken.None).AsTask());

        Assert.Contains("повреждённый", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SlowServerEndsInATimeoutMessage()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}") { Delay = TimeSpan.FromSeconds(30) };
        var transcriber = new OpenAiTranscriber(
            new HttpClient(handler),
            new FixedKey(ApiKey),
            requestTimeout: TimeSpan.FromMilliseconds(80));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transcriber.TranscribeAsync(Speech(), CancellationToken.None).AsTask());

        Assert.Contains("не ответил вовремя", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationStaysACancellation()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}") { Delay = TimeSpan.FromSeconds(30) };
        var transcriber = Create(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transcriber.TranscribeAsync(Speech(), cancellation.Token).AsTask());
    }

    [Fact]
    public async Task NetworkFailureAsksToCheckTheConnection()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}") { Throw = new HttpRequestException("down") };
        var transcriber = Create(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transcriber.TranscribeAsync(Speech(), CancellationToken.None).AsTask());

        Assert.Contains("подключение к интернету", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlainHttpEndpointIsRefused() =>
        Assert.Throws<ArgumentException>(() => new OpenAiTranscriber(
            new HttpClient(),
            new FixedKey(ApiKey),
            new Uri("http://api.example.test/v1/audio/transcriptions")));

    [Fact]
    public void RequestTimeoutMustBePositive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenAiTranscriber(
            new HttpClient(),
            new FixedKey(ApiKey),
            requestTimeout: TimeSpan.Zero));

    [Fact]
    public void WaveEncodingWritesAValidPcm16MonoHeader()
    {
        var audio = new RecordedAudio([0f, 1f, -1f, 2f, -2f], 16_000);

        var wave = OpenAiTranscriber.EncodePcm16Wave(audio);

        Assert.Equal(44 + (5 * 2), wave.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(1, BitConverter.ToInt16(wave, 20));
        Assert.Equal(1, BitConverter.ToInt16(wave, 22));
        Assert.Equal(16_000, BitConverter.ToInt32(wave, 24));
        Assert.Equal(10, BitConverter.ToInt32(wave, 40));
        Assert.Equal(0, BitConverter.ToInt16(wave, 44));
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(wave, 46));
        Assert.Equal(-short.MaxValue, BitConverter.ToInt16(wave, 48));
        // Samples outside [-1, 1] are clamped, never wrapped around.
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(wave, 50));
        Assert.Equal(-short.MaxValue, BitConverter.ToInt16(wave, 52));
    }

    [Fact]
    public void RecordingsTooLongForTheUploadLimitAreRejected()
    {
        var samples = new float[(OpenAiTranscriber.MaximumUploadBytes / 2) + 1];

        Assert.Throws<InvalidOperationException>(
            () => OpenAiTranscriber.EncodePcm16Wave(new RecordedAudio(samples, 16_000)));
    }

    private static OpenAiTranscriber Create(RecordingHandler handler, string? language = null) =>
        new(new HttpClient(handler), new FixedKey(ApiKey), language: language);

    private static RecordedAudio Speech() => new(new float[16_000], 16_000);

    private sealed class FixedKey(string key) : IOpenAiApiKeyProvider
    {
        public string GetApiKey() => key;
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);

    private sealed class RecordingHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        public TimeSpan Delay { get; init; }

        public Exception? Throw { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : Encoding.Latin1.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                body));

            if (Throw is not null)
            {
                throw Throw;
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}

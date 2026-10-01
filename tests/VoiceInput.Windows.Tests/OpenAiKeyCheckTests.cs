using System.Net;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests.Transcription;

public sealed class OpenAiKeyCheckTests
{
    private const string ApiKey = "sk-test-0123456789abcdef0123456789abcdef0123";

    [Fact]
    public async Task AcceptedKeyIsReportedAsWorking()
    {
        var handler = new StubHandler(HttpStatusCode.OK);

        var result = await OpenAiKeyCheck.CheckAsync(new HttpClient(handler), ApiKey, CancellationToken.None);

        Assert.Equal(OpenAiKeyCheckStatus.Accepted, result.Status);
        Assert.Equal($"Bearer {ApiKey}", handler.Authorization);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(OpenAiKeyCheck.DefaultEndpoint, handler.Uri);
    }

    [Fact]
    public async Task SurroundingWhitespaceInTheKeyIsIgnored()
    {
        var handler = new StubHandler(HttpStatusCode.OK);

        await OpenAiKeyCheck.CheckAsync(new HttpClient(handler), $"  {ApiKey}\r\n", CancellationToken.None);

        Assert.Equal($"Bearer {ApiKey}", handler.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, OpenAiKeyCheckStatus.Rejected, "отклонил ключ")]
    [InlineData(HttpStatusCode.Forbidden, OpenAiKeyCheckStatus.Restricted, "ограниченные права")]
    [InlineData(HttpStatusCode.TooManyRequests, OpenAiKeyCheckStatus.Inconclusive, "429")]
    [InlineData(HttpStatusCode.BadGateway, OpenAiKeyCheckStatus.Inconclusive, "502")]
    public async Task EveryStatusHasItsOwnExplanation(HttpStatusCode status, OpenAiKeyCheckStatus expected, string fragment)
    {
        var result = await OpenAiKeyCheck.CheckAsync(
            new HttpClient(new StubHandler(status)),
            ApiKey,
            CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Contains(fragment, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObviousNonKeyIsRejectedWithoutANetworkCall()
    {
        var handler = new StubHandler(HttpStatusCode.OK);

        var result = await OpenAiKeyCheck.CheckAsync(new HttpClient(handler), "hello", CancellationToken.None);

        Assert.Equal(OpenAiKeyCheckStatus.Rejected, result.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task NetworkFailureIsInconclusiveNotRejected()
    {
        var handler = new StubHandler(HttpStatusCode.OK) { Throw = new HttpRequestException("down") };

        var result = await OpenAiKeyCheck.CheckAsync(new HttpClient(handler), ApiKey, CancellationToken.None);

        Assert.Equal(OpenAiKeyCheckStatus.Inconclusive, result.Status);
        Assert.Contains("подключение к интернету", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationIsNotSwallowed()
    {
        var handler = new StubHandler(HttpStatusCode.OK) { Delay = TimeSpan.FromSeconds(30) };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(60));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OpenAiKeyCheck.CheckAsync(new HttpClient(handler), ApiKey, cancellation.Token));
    }

    [Fact]
    public async Task PlainHttpEndpointIsRefused() =>
        await Assert.ThrowsAsync<ArgumentException>(() => OpenAiKeyCheck.CheckAsync(
            new HttpClient(new StubHandler(HttpStatusCode.OK)),
            ApiKey,
            CancellationToken.None,
            new Uri("http://api.example.test/v1/models")));

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? Authorization { get; private set; }

        public HttpMethod? Method { get; private set; }

        public Uri? Uri { get; private set; }

        public TimeSpan Delay { get; init; }

        public Exception? Throw { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Authorization = request.Headers.Authorization?.ToString();
            Method = request.Method;
            Uri = request.RequestUri;
            if (Throw is not null)
            {
                throw Throw;
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return new HttpResponseMessage(status);
        }
    }
}

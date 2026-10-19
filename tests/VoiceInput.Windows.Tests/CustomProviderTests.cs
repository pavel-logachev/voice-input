using System.Net;
using VoiceInput.Core.Audio;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests;

public sealed class CustomProviderTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8000/v1", true)]
    [InlineData("http://[::1]/v1", true)]
    [InlineData("http://192.168.1.3/v1", true)]
    [InlineData("http://172.16.0.1/v1", true)]
    [InlineData("http://10.0.0.1/v1", true)]
    [InlineData("http://example.com/v1", false)]
    [InlineData("http://172.32.0.1/v1", false)]
    [InlineData("https://example.com/v1", true)]
    public void EndpointTransportPolicy(string url, bool allowed)
    {
        if (allowed) Assert.NotNull(CustomEndpointPolicy.NormalizeBaseUrl(url));
        else Assert.Throws<ArgumentException>(() => CustomEndpointPolicy.NormalizeBaseUrl(url));
    }

    [Fact]
    public async Task CustomMultipartUsesModelLanguageAndOptionalKey()
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("http://localhost:8000/v1/audio/transcriptions", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("my-model", body, StringComparison.Ordinal);
            Assert.Contains("ru", body, StringComparison.Ordinal);
            Assert.Contains("dictation.wav", body, StringComparison.Ordinal);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"text\":\" готово \"}") };
        }));
        var transcriber = new OpenAiTranscriber(http, new EmptyKey(), new Uri("http://localhost:8000/v1/audio/transcriptions"), model: "my-model", language: "ru", customProvider: true);
        Assert.Equal("готово", await transcriber.TranscribeAsync(new RecordedAudio(new float[16000], 16000), CancellationToken.None));
    }

    [Theory]
    [InlineData(404, CustomProviderValidationStatus.ReachableModelNotVerified)]
    [InlineData(405, CustomProviderValidationStatus.ReachableModelNotVerified)]
    [InlineData(501, CustomProviderValidationStatus.ReachableModelNotVerified)]
    [InlineData(200, CustomProviderValidationStatus.ModelVerified)]
    [InlineData(401, CustomProviderValidationStatus.Failed)]
    public async Task ValidateHandlesUnsupportedModelsEndpoint(int status, CustomProviderValidationStatus expected)
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("https://example.com/v1/models", request.RequestUri!.AbsoluteUri);
            Assert.Equal("token", request.Headers.Authorization!.Parameter);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{\"data\":[{\"id\":\"m\"}]}") });
        }));
        var result = await CustomProviderValidation.ValidateAsync(http, "https://example.com/v1/", "m", "token", CancellationToken.None);
        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(401, TranscriptionErrorCode.Unauthorized)]
    [InlineData(429, TranscriptionErrorCode.RateLimited)]
    [InlineData(400, TranscriptionErrorCode.InvalidRequest)]
    [InlineData(503, TranscriptionErrorCode.ServiceUnavailable)]
    public async Task CustomErrorsHaveStableCodes(int status, TranscriptionErrorCode code)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))));
        var transcriber = new OpenAiTranscriber(http, new EmptyKey(), new Uri("https://example.com/v1/audio/transcriptions"), customProvider: true);
        var error = await Assert.ThrowsAsync<TranscriptionException>(async () => await transcriber.TranscribeAsync(new RecordedAudio(new float[16000], 16000), CancellationToken.None));
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("OpenAI", error.Message, StringComparison.Ordinal);
        Assert.Equal(VoiceInputStrings.TranscriptionError(code), error.Message);
        Assert.DoesNotContain("HTTP", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomNetworkFailureUsesApprovedCopyWithoutReplacementArtifacts()
    {
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("private diagnostic")));
        var transcriber = new OpenAiTranscriber(http, new EmptyKey(), new Uri("https://example.com/v1/audio/transcriptions"), customProvider: true);
        var error = await Assert.ThrowsAsync<TranscriptionException>(async () => await transcriber.TranscribeAsync(new RecordedAudio(new float[16000], 16000), CancellationToken.None));
        Assert.Equal("Нет связи с сервером. Проверьте адрес и сеть.", error.Message);
        Assert.Equal(TranscriptionErrorCode.Network, error.Code);
    }

    [Fact]
    public void CustomKeyIsIndependentEncryptedAndOptional()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keys-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "custom.dpapi");
        try
        {
            var key = new DpapiCustomApiKeyProvider(path);
            Assert.Equal("", key.GetApiKey());
            key.Save("private-token-not-openai");
            Assert.Equal("private-token-not-openai", key.GetApiKey());
            Assert.DoesNotContain("private-token-not-openai", File.ReadAllText(path), StringComparison.Ordinal);
            key.Save("");
            Assert.False(key.HasKey);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class EmptyKey : IOpenAiApiKeyProvider { public string GetApiKey() => ""; }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }
}

using System.Net;
using VoiceInput.App;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests;

public sealed class CustomKeyRegressionTests
{
    [Theory]
    [InlineData("bad\nkey")]
    [InlineData("key\r\n")]
    public async Task InvalidKeyIsRejectedBeforeHeadersAndBeforeNetwork(string key)
    {
        using var http = new HttpClient(new NoNetwork());
        var result = await CustomProviderValidation.ValidateAsync(http, "http://localhost/v1", "m", key, CancellationToken.None);
        Assert.Equal(CustomProviderValidationStatus.Failed, result.Status);
        Assert.Equal(TranscriptionErrorCode.InvalidKeyCharacters, result.ErrorCode);
    }

    [Fact]
    public async Task ServiceSaveAndCheckUseApprovedInvalidCharacterMessage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VoiceInput-tests", Guid.NewGuid().ToString("N"));
        await using var service = new EngineSettingsServices(new LocalModelManager(directory),
            new DpapiCustomApiKeyProvider(Path.Combine(directory, "key")), new NoNetwork());
        try
        {
            Assert.Equal("В ключе недопустимые символы, например перевод строки. Вставьте ключ одной строкой.", service.SaveKey("bad\nkey"));
            var result = await service.CheckAsync("http://localhost/v1", "m", "bad\nkey");
            Assert.Equal(CustomProviderValidationStatus.Failed, result.Status);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SaveFailureUsesFinalCopy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VoiceInput-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var blocker = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(blocker, "block");
        await using var service = new EngineSettingsServices(new LocalModelManager(directory), new DpapiCustomApiKeyProvider(Path.Combine(blocker, "key")));
        try
        {
            Assert.Equal("Не удалось сохранить ключ: ошибка диска или шифрования. Повторите попытку.", service.SaveKey("token"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CancelCheckCancelsPendingHttpRequest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VoiceInput-tests", Guid.NewGuid().ToString("N"));
        var handler = new PendingNetwork();
        await using var service = new EngineSettingsServices(new LocalModelManager(directory), new DpapiCustomApiKeyProvider(Path.Combine(directory, "key")), handler);
        var check = service.CheckAsync("http://localhost/v1", "m", null);
        await handler.Started.Task;
        service.CancelCheck();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.WaitAsync(TimeSpan.FromSeconds(1)));
    }
    private sealed class PendingNetwork : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return new(HttpStatusCode.OK);
        }
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Invalid key must never reach network");
    }
}

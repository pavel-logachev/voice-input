using VoiceInput.App;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests.Product;

/// <summary>The settings window shows approved strings only, with the agreed number formats.</summary>
public sealed class EngineSettingsCopyTests
{
    private const long ModelSize = 183_948_704;

    [Fact]
    public void DownloadLineUsesWholeMegabytesAndOneDecimalSpeed()
    {
        var text = EngineCopy.LocalState(new LocalModelProgress(LocalModelState.Downloading, 73_400_000, ModelSize, 5_200_000), ModelSize);

        Assert.Equal(
            VoiceInputStrings.Get("local.state.downloading")
                .Replace("{downloaded}", "73", StringComparison.Ordinal)
                .Replace("{total}", "184", StringComparison.Ordinal)
                .Replace("{speed}", "5,2", StringComparison.Ordinal),
            text);
    }

    [Fact]
    public void ReceivedPartNeverReadsAsCompleteEarly()
    {
        var text = EngineCopy.LocalState(new LocalModelProgress(LocalModelState.Paused, ModelSize - 1, ModelSize), ModelSize);

        Assert.Contains("183", text, StringComparison.Ordinal);
        Assert.Equal("184", EngineCopy.Megabytes(ModelSize));
    }

    [Fact]
    public void DiskSpaceLineNamesTheAmountWithItsUnit()
    {
        var text = EngineCopy.LocalState(
            new LocalModelProgress(LocalModelState.Failed, 0, ModelSize, FailureReason: ModelFailureReason.InsufficientDiskSpace),
            ModelSize);

        Assert.DoesNotContain("{required}", text, StringComparison.Ordinal);
        Assert.Contains("193\u00A0МБ", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LocalModelState.NotInstalled, "local.action.download")]
    [InlineData(LocalModelState.Downloading, "local.action.pause")]
    [InlineData(LocalModelState.Paused, "local.action.resume")]
    [InlineData(LocalModelState.Failed, "local.action.retry")]
    [InlineData(LocalModelState.Ready, "local.action.delete")]
    [InlineData(LocalModelState.Verifying, null)]
    public void EachModelStateOffersItsAction(LocalModelState state, string? key) =>
        Assert.Equal(key, EngineCopy.LocalActionKey(state));

    [Theory]
    [InlineData(CustomProviderValidationStatus.ModelVerified, null, "custom.check.modelVerified")]
    [InlineData(CustomProviderValidationStatus.ReachableModelNotVerified, null, "custom.check.reachableModelNotVerified")]
    [InlineData(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.Unauthorized, "custom.check.failed.unauthorized")]
    [InlineData(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.Network, "custom.check.failed.network")]
    [InlineData(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.Timeout, "custom.check.failed.timeout")]
    [InlineData(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.ServiceUnavailable, "custom.check.failed.serviceUnavailable")]
    [InlineData(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.InvalidResponse, "custom.check.failed.invalidResponse")]
    [InlineData(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.RateLimited, "overlay.error.custom.rateLimited")]
    [InlineData(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.InvalidRequest, "overlay.error.custom.invalidRequest")]
    public void EachCheckResultHasItsApprovedLine(CustomProviderValidationStatus status, TranscriptionErrorCode? code, string key) =>
        Assert.Equal(VoiceInputStrings.Get(key), EngineCopy.CheckResult(new CustomProviderValidationResult(status, code)));

    [Theory]
    [InlineData("", "custom.url.error.required")]
    [InlineData("   ", "custom.url.error.required")]
    [InlineData("api.groq.com/openai/v1", "custom.url.error.invalid")]
    [InlineData("ftp://example.com/v1", "custom.url.error.invalid")]
    [InlineData("https://user:pass@example.com/v1", "custom.url.error.invalid")]
    [InlineData("https://example.com/v1?x=1", "custom.url.error.invalid")]
    [InlineData("http://speech.example.com/v1", "custom.url.error.httpNotAllowed")]
    [InlineData("http://localhost:8000/v1", null)]
    [InlineData("http://192.168.1.20:8000/v1", null)]
    [InlineData("https://api.groq.com/openai/v1", null)]
    public void AddressErrorsFollowTheEndpointPolicy(string value, string? key)
    {
        Assert.Equal(key, EngineCopy.UrlErrorKey(value));

        // Whatever the window accepts, the backend accepts too.
        if (key is null)
        {
            _ = CustomEndpointPolicy.NormalizeBaseUrl(value);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => CustomEndpointPolicy.NormalizeBaseUrl(value));
        }
    }

    [Fact]
    public void ModelNameIsRequired()
    {
        Assert.Equal("custom.model.error.required", EngineCopy.ModelErrorKey(" "));
        Assert.Null(EngineCopy.ModelErrorKey("whisper-1"));
    }
}

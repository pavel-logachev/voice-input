using VoiceInput.Core.Activation;
using VoiceInput.Windows.Input;

namespace VoiceInput.Windows.Tests.Input;

public sealed class WindowsUnicodeTextInserterTests
{
    [Fact]
    public async Task InserterRejectsTargetWhenForegroundWindowChanged()
    {
        var api = new RecordingInputApi((nint)99);
        var inserter = new WindowsUnicodeTextInserter(api);

        await Assert.ThrowsAsync<TargetFocusChangedException>(
            () => inserter.InsertAsync(
                new InputTarget((nint)42, 7),
                "text",
                CancellationToken.None).AsTask());

        Assert.Empty(api.SentStrokes);
    }

    private sealed class RecordingInputApi(nint foregroundWindow) : IWindowsInputApi
    {
        public List<UnicodeKeyStroke> SentStrokes { get; } = [];

        public nint GetForegroundWindow() => foregroundWindow;

        public void Send(IReadOnlyList<UnicodeKeyStroke> strokes) => SentStrokes.AddRange(strokes);
    }
}

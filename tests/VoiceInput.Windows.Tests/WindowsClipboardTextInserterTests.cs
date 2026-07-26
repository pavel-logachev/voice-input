using VoiceInput.Core.Activation;
using VoiceInput.Windows.Input;

namespace VoiceInput.Windows.Tests.Input;

public sealed class WindowsClipboardTextInserterTests
{
    private static readonly InputTarget Target = new((nint)0x1234, 42);

    [Fact]
    public async Task SuccessfulPasteRestoresTheOriginalClipboard()
    {
        var api = new FakeClipboardInsertionApi(Target.WindowHandle);
        var inserter = new WindowsClipboardTextInserter(
            api,
            new FakePasteDelay(() => api.Trace.Add("delay")));

        await inserter.InsertAsync(Target, "Русский текст", CancellationToken.None);

        Assert.Equal(
            ["capture", "set:Русский текст", "paste", "delay", "restore"],
            api.Trace);
    }

    [Fact]
    public async Task NewerUserClipboardContentIsNeverOverwritten()
    {
        var api = new FakeClipboardInsertionApi(Target.WindowHandle);
        var delay = new FakePasteDelay(() =>
        {
            api.Trace.Add("delay");
            api.TemporaryFormatPresent = false;
        });
        var inserter = new WindowsClipboardTextInserter(api, delay);

        await inserter.InsertAsync(Target, "Текст", CancellationToken.None);

        Assert.Equal(["capture", "set:Текст", "paste", "delay"], api.Trace);
    }

    [Fact]
    public async Task FocusChangeAfterClipboardWriteRestoresWithoutPasting()
    {
        var api = new FakeClipboardInsertionApi(Target.WindowHandle)
        {
            ForegroundAfterClipboardWrite = (nint)0x9999,
        };
        var inserter = new WindowsClipboardTextInserter(api, new FakePasteDelay());

        await Assert.ThrowsAsync<TargetFocusChangedException>(
            () => inserter.InsertAsync(Target, "Текст", CancellationToken.None).AsTask());

        Assert.Equal(["capture", "set:Текст", "restore"], api.Trace);
    }

    [Fact]
    public async Task CancellationBeforeMutationLeavesClipboardUntouched()
    {
        var api = new FakeClipboardInsertionApi(Target.WindowHandle);
        var inserter = new WindowsClipboardTextInserter(api, new FakePasteDelay());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inserter.InsertAsync(Target, "Текст", cancellation.Token).AsTask());

        Assert.Empty(api.Trace);
    }

    private sealed class FakeClipboardInsertionApi : IClipboardInsertionApi
    {
        private readonly nint foregroundWindow;
        private bool clipboardWritten;

        public FakeClipboardInsertionApi(nint foregroundWindow)
        {
            this.foregroundWindow = foregroundWindow;
            ForegroundAfterClipboardWrite = foregroundWindow;
        }

        public List<string> Trace { get; } = [];

        public bool TemporaryFormatPresent { get; set; }

        public nint ForegroundAfterClipboardWrite { get; init; }

        public nint GetForegroundWindow() => clipboardWritten ? ForegroundAfterClipboardWrite : foregroundWindow;

        public ClipboardSnapshot CaptureClipboard()
        {
            Trace.Add("capture");
            return new ClipboardSnapshot(new object());
        }

        public void SetText(string text)
        {
            Trace.Add($"set:{text}");
            clipboardWritten = true;
            TemporaryFormatPresent = true;
        }

        public bool IsTemporaryClipboardCurrent() => TemporaryFormatPresent;

        public void SendPaste() => Trace.Add("paste");

        public void RestoreClipboard(ClipboardSnapshot snapshot)
        {
            Assert.NotNull(snapshot.State);
            Trace.Add("restore");
            TemporaryFormatPresent = false;
        }
    }

    private sealed class FakePasteDelay(Action? onDelay = null) : IClipboardPasteDelay
    {
        public ValueTask DelayAsync()
        {
            onDelay?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}

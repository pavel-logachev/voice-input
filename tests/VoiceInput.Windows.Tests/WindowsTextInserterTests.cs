using VoiceInput.Core.Activation;
using VoiceInput.Windows.Input;

namespace VoiceInput.Windows.Tests.Input;

public sealed class WindowsTextInserterTests
{
    private static readonly InputTarget Target = new((nint)0x1234, 42);

    [Theory]
    [InlineData("Edit")]
    [InlineData("RichEditD2DPT")]
    [InlineData("WindowsForms10.EDIT.app.0.2bf8098_r6_ad1")]
    public async Task NativeTextControlsUseDirectSelectionReplacement(string className)
    {
        var api = new FakeNativeTextControlApi(Target.WindowHandle, (nint)0x2345, className);
        var fallback = new FakeTextInserter();
        var inserter = new WindowsTextInserter(api, fallback);

        await inserter.InsertAsync(Target, "Русский текст", CancellationToken.None);

        Assert.Equal(["replace:9029:Русский текст"], api.Trace);
        Assert.False(fallback.WasCalled);
    }

    [Fact]
    public async Task CustomControlsUseClipboardFallback()
    {
        var api = new FakeNativeTextControlApi(Target.WindowHandle, (nint)0x2345, "Chrome_RenderWidgetHostHWND");
        var fallback = new FakeTextInserter();
        var inserter = new WindowsTextInserter(api, fallback);

        await inserter.InsertAsync(Target, "Текст", CancellationToken.None);

        Assert.True(fallback.WasCalled);
        Assert.Empty(api.Trace);
    }

    [Fact]
    public async Task FocusOutsideCapturedWindowUsesFallbackSafetyChecks()
    {
        var api = new FakeNativeTextControlApi(Target.WindowHandle, (nint)0x2345, "Edit")
        {
            FocusIsChild = false,
        };
        var fallback = new FakeTextInserter();
        var inserter = new WindowsTextInserter(api, fallback);

        await inserter.InsertAsync(Target, "Текст", CancellationToken.None);

        Assert.True(fallback.WasCalled);
        Assert.Empty(api.Trace);
    }

    private sealed class FakeNativeTextControlApi(
        nint foreground,
        nint focusedControl,
        string className) : INativeTextControlApi
    {
        public List<string> Trace { get; } = [];

        public bool FocusIsChild { get; init; } = true;

        public nint GetForegroundWindow() => foreground;

        public nint GetFocusedControl(nint topLevelWindow) => focusedControl;

        public bool IsChild(nint parent, nint child) => FocusIsChild;

        public string GetClassName(nint windowHandle) => className;

        public void ReplaceSelection(nint control, string text) =>
            Trace.Add($"replace:{control}:{text}");
    }

    private sealed class FakeTextInserter : ITextInserter
    {
        public bool WasCalled { get; private set; }

        public ValueTask InsertAsync(InputTarget target, string text, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }
}

using System.Diagnostics;
using VoiceInput.Core.Activation;
using VoiceInput.Windows.Audio;
using VoiceInput.Windows.Transcription;

var options = ParseArguments(args);
await using var client = new GigaAmWorkerClient(
    options.WorkerExecutable,
    options.RuntimeDirectory,
    options.ModelPath);
await client.StartAsync(CancellationToken.None);

var inserter = new RecordingTextInserter();
var workflow = new DictationWorkflow(
    new FixedTargetCapture(),
    new NoOpOverlay(),
    new ImmediateReleaseGate(),
    inserter,
    new NoOpDelay(),
    new PcmFixtureAudioRecorder(options.PcmPath),
    new SegmentingTranscriber(client));

var stopwatch = Stopwatch.StartNew();
var accepted = await workflow.TryActivateAsync(CancellationToken.None);
stopwatch.Stop();

if (!accepted || !string.Equals(inserter.Text, options.ExpectedText, StringComparison.Ordinal))
{
    Console.Error.WriteLine($"ASR_E2E_FAIL expected={options.ExpectedText} actual={inserter.Text}");
    return 1;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"ASR_E2E_PASS elapsed_ms={stopwatch.ElapsedMilliseconds} text={inserter.Text}");
return 0;

static Options ParseArguments(string[] arguments)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length || !arguments[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Arguments must use --name value pairs.");
        }

        values[arguments[index][2..]] = arguments[index + 1];
    }

    return new Options(
        Require("worker"),
        Require("runtime"),
        Require("model"),
        Require("pcm"),
        Require("expected"));

    string Require(string name) => values.TryGetValue(name, out var value) && value.Length > 0
        ? value
        : throw new ArgumentException($"Missing required --{name} argument.");
}

internal sealed record Options(
    string WorkerExecutable,
    string RuntimeDirectory,
    string ModelPath,
    string PcmPath,
    string ExpectedText);

internal sealed class FixedTargetCapture : IInputTargetCapture
{
    public InputTarget Capture() => new((nint)42, 7);
}

internal sealed class NoOpOverlay : IActivationOverlay
{
    public void Show(ActivationVisualState state)
    {
    }

    public void Hide()
    {
    }
}

internal sealed class ImmediateReleaseGate : IModifierReleaseGate
{
    public ValueTask WaitAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class RecordingTextInserter : ITextInserter
{
    public string? Text { get; private set; }

    public ValueTask InsertAsync(InputTarget target, string text, CancellationToken cancellationToken)
    {
        Text = text;
        return ValueTask.CompletedTask;
    }
}

internal sealed class NoOpDelay : IAsyncDelay
{
    public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

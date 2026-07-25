using System.IO.Pipes;
using VoiceInput.Asr.Worker;

const byte TranscribeCommand = 1;
const byte ShutdownCommand = 2;
const int RequiredSampleRate = 16_000;
const int MaximumSamples = RequiredSampleRate * 25;

var options = ParseArguments(args);
using var model = new GigaAmSession(options.RuntimeDirectory, options.ModelPath);
Console.Error.WriteLine($"VOICE_INPUT_ASR_READY backend={model.Backend}");

using var pipe = new NamedPipeServerStream(
    options.PipeName,
    PipeDirection.InOut,
    1,
    PipeTransmissionMode.Byte,
    PipeOptions.None);
pipe.WaitForConnection();

using var reader = new BinaryReader(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
using var writer = new BinaryWriter(pipe, System.Text.Encoding.UTF8, leaveOpen: true);

while (pipe.IsConnected)
{
    byte command;
    try
    {
        command = reader.ReadByte();
    }
    catch (EndOfStreamException)
    {
        break;
    }

    if (command == ShutdownCommand)
    {
        break;
    }

    if (command != TranscribeCommand)
    {
        WriteError(writer, $"Unsupported worker command: {command}.");
        continue;
    }

    try
    {
        var sampleRate = reader.ReadInt32();
        var sampleCount = reader.ReadInt32();
        if (sampleRate != RequiredSampleRate)
        {
            throw new InvalidDataException($"Expected {RequiredSampleRate} Hz audio, received {sampleRate} Hz.");
        }

        if (sampleCount <= 0 || sampleCount > MaximumSamples)
        {
            throw new InvalidDataException($"Sample count {sampleCount} is outside the supported range.");
        }

        var byteCount = checked(sampleCount * sizeof(float));
        var bytes = reader.ReadBytes(byteCount);
        if (bytes.Length != byteCount)
        {
            throw new EndOfStreamException("The PCM payload ended before all samples were received.");
        }

        var samples = new float[sampleCount];
        Buffer.BlockCopy(bytes, 0, samples, 0, byteCount);
        var text = model.Transcribe(samples);

        writer.Write((byte)0);
        writer.Write(text);
        writer.Flush();
    }
    catch (Exception exception)
    {
        WriteError(writer, exception.Message);
    }
}

static void WriteError(BinaryWriter writer, string message)
{
    writer.Write((byte)1);
    writer.Write(message);
    writer.Flush();
}

static WorkerOptions ParseArguments(string[] arguments)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length || !arguments[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Worker arguments must use --name value pairs.");
        }

        values[arguments[index][2..]] = arguments[index + 1];
    }

    return new WorkerOptions(
        Require("pipe"),
        Require("runtime"),
        Require("model"));

    string Require(string name) => values.TryGetValue(name, out var value) && value.Length > 0
        ? value
        : throw new ArgumentException($"Missing required --{name} argument.");
}

internal sealed record WorkerOptions(string PipeName, string RuntimeDirectory, string ModelPath);

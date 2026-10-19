using System.Runtime.InteropServices;
using System.Text;

namespace VoiceInput.Windows.Transcription;

/// <summary>Bounded binary wire format shared with the isolated worker.</summary>
public static class AsrWorkerProtocol
{
    public static async ValueTask WriteRequestAsync(Stream stream, ReadOnlyMemory<float> samples, int sampleRate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)1);
            writer.Write(sampleRate);
            writer.Write(samples.Length);
            writer.Write(MemoryMarshal.AsBytes(samples.Span));
        }
        await stream.WriteAsync(memory.GetBuffer().AsMemory(0, (int)memory.Length), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<string> ReadResponseAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var single = new byte[1];
        await stream.ReadExactlyAsync(single, cancellationToken).ConfigureAwait(false);
        var status = single[0];
        var length = 0;
        for (var shift = 0; ; shift += 7)
        {
            await stream.ReadExactlyAsync(single, cancellationToken).ConfigureAwait(false);
            if (shift >= 28) throw new InvalidDataException("Invalid worker string length.");
            length |= (single[0] & 0x7f) << shift;
            if (length > 1024 * 1024) throw new InvalidDataException("Worker response is too large.");
            if ((single[0] & 0x80) == 0) break;
        }
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        if (status != 0) throw new InvalidDataException("Local ASR rejected the request.");
        try { return new UTF8Encoding(false, true).GetString(payload); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Invalid UTF-8 worker response.", exception); }
    }
}

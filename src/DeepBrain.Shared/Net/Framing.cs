using System.Buffers.Binary;

namespace DeepBrain.Shared.Net;

public static class Framing
{
    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        // C# 12: никакого stackalloc/Span в async
        var lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, payload.Length);

        await stream.WriteAsync(lenBuf.AsMemory(0, 4), ct).ConfigureAwait(false);

        if (payload.Length > 0)
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);

        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<byte[]?> ReadFrameAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        int read = await ReadUpToAsync(stream, lenBuf, ct).ConfigureAwait(false);
        if (read == 0) return null; // disconnected cleanly

        if (read < 4)
            throw new EndOfStreamException("Disconnected while reading frame length.");

        int len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        if (len < 0 || len > maxBytes)
            throw new InvalidDataException($"Frame length {len} is invalid (max {maxBytes}).");

        if (len == 0) return Array.Empty<byte>();

        var payload = new byte[len];
        await ReadExactlyAsync(stream, payload, ct).ConfigureAwait(false);
        return payload;
    }

    private static async Task<int> ReadUpToAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (n == 0)
                return total;
            total += n;
        }
        return total;
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (n == 0)
                throw new EndOfStreamException("Disconnected while reading frame payload.");
            total += n;
        }
    }
}

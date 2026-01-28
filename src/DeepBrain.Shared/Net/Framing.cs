using System.Buffers.Binary;
using System.Text;

namespace DeepBrain.Shared.Net;

public static class Framing
{
    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        Span<byte> lenBuf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, payload.Length);

        await stream.WriteAsync(lenBuf, ct);
        if (payload.Length > 0)
            await stream.WriteAsync(payload, ct);

        await stream.FlushAsync(ct);
    }

    public static async Task<byte[]?> ReadFrameAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        int read = await ReadExactlyAsync(stream, lenBuf, ct);
        if (read == 0) return null; // disconnected cleanly

        int len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        if (len < 0 || len > maxBytes)
            throw new InvalidDataException($"Frame length {len} is invalid (max {maxBytes}).");

        if (len == 0) return Array.Empty<byte>();

        var payload = new byte[len];
        await ReadExactlyAsync(stream, payload, ct);
        return payload;
    }

    private static async Task<int> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (n == 0)
                return total; // disconnected
            total += n;
        }
        return total;
    }
}

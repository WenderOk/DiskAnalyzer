using System.Buffers.Binary;
using System.Text.Json;

namespace DiskAnalyzer.Worker.Contracts;

public static class Frames
{
    private const int MaxFrameSize = 128 * 1024 * 1024;

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions.Default);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        await stream.WriteAsync(header.AsMemory(), ct);
        await stream.WriteAsync(payload.AsMemory(), ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        var headerRead = await ReadExactAsync(stream, header, ct);
        if (headerRead < header.Length)
            return default;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrameSize)
            throw new IOException($"Некорректная длина кадра: {length}");

        var payload = new byte[length];
        var payloadRead = await ReadExactAsync(stream, payload, ct);
        if (payloadRead < payload.Length)
            return default;

        return JsonSerializer.Deserialize<T>(payload, JsonOptions.Default);
    }

    private static async Task<int> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..], ct);
            if (read == 0)
                return total;
            total += read;
        }
        return total;
    }
}

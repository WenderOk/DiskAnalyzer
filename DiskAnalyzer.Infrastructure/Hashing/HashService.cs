using System.Security.Cryptography;
using DiskAnalyzer.Application.Abstractions;

namespace DiskAnalyzer.Infrastructure.Hashing;

public sealed class HashService : IHashService
{
    public async Task<string> ComputeHashAsync(Stream stream, CancellationToken ct = default)
    {
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<string> ComputeFileHashAsync(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 81920, useAsync: true);
        return await ComputeHashAsync(stream, ct);
    }

    public async Task<string> ComputePartialFileHashAsync(
        string path, long bytesToRead, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 81920, useAsync: true);

        var available = stream.Length;
        var toRead = (int)Math.Min(bytesToRead, available);
        if (toRead <= 0)
            return string.Empty;

        var buffer = new byte[toRead];
        var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), ct);
        var hash = SHA256.HashData(buffer.AsSpan(0, read));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
namespace DiskAnalyzer.Application.Abstractions;

public interface IHashService
{
    Task<string> ComputeHashAsync(Stream stream, CancellationToken ct = default);
    Task<string> ComputeFileHashAsync(string path, CancellationToken ct = default);
    Task<string> ComputePartialFileHashAsync(string path, long bytesToRead, CancellationToken ct = default);
}
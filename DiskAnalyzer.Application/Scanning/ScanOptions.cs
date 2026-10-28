namespace DiskAnalyzer.Application.Scanning;

public sealed class ScanOptions
{
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount * 2;
    public long ExpectedTotalBytes { get; init; }

    public static ScanOptions Default { get; } = new();
}
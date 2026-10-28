namespace DiskAnalyzer.Worker.Contracts;

public sealed class WorkerRequest
{
    public required string RequestId { get; init; }
    public required string Command { get; init; }

    public string? Path { get; init; }
    public string? ScanId { get; init; }
    public ScanFilterPayload? Filter { get; init; }

    public int? MaxDegreeOfParallelism { get; init; }
    public int? BatchSize { get; init; }
    public bool? ExtractPreview { get; init; }
}
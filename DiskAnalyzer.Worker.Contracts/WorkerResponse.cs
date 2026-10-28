namespace DiskAnalyzer.Worker.Contracts;

public sealed class WorkerResponse
{
    public required string RequestId { get; init; }
    public required WorkerMessageType Type { get; init; }

    public ScanProgressPayload? Progress { get; init; }
    public ScanResultPayload? ScanResult { get; init; }
    public List<DuplicateGroupPayload>? Duplicates { get; init; }
    public IndexResultPayload? IndexResult { get; init; }
    public string? Error { get; init; }
}
namespace DiskAnalyzer.Worker.Contracts;

public enum WorkerMessageType
{
    Progress,
    Pong,
    ScanCompleted,
    DedupCompleted,
    IndexCompleted,
    Cancelled,
    Error
}
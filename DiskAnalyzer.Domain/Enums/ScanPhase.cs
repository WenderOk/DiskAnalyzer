namespace DiskAnalyzer.Domain.Enums;

public enum ScanPhase
{
    Enumerating,
    Hashing,
    Finalizing,
    Completed,
    Cancelled,
    Failed
}
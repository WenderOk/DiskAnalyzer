using DiskAnalyzer.Domain.Enums;

namespace DiskAnalyzer.Domain.Models;

public sealed class ScanProgress
{
    public required ScanPhase Phase { get; init; }
    public string CurrentPath { get; init; } = string.Empty;
    public long ProcessedFiles { get; init; }
    public long ProcessedFolders { get; init; }
    public FileSize ProcessedSize { get; init; }
    public double Percent { get; init; }
    public string? Message { get; init; }

    public static ScanProgress Empty { get; } = new()
    {
        Phase = ScanPhase.Enumerating,
        Percent = 0
    };
}
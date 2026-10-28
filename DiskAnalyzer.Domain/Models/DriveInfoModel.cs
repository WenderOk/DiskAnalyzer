using DiskAnalyzer.Domain.Enums;

namespace DiskAnalyzer.Domain.Models;

public sealed class DriveInfoModel
{
    public required string Name { get; init; }
    public required string RootPath { get; init; }
    public required string Label { get; init; }
    public required string FileSystem { get; init; }
    public DriveKind Kind { get; init; }
    public FileSize TotalSize { get; init; }
    public FileSize FreeSpace { get; init; }
    public bool IsReady { get; init; }

    public FileSize UsedSpace => new(TotalSize.Bytes - FreeSpace.Bytes);

    public double UsedPercent => TotalSize.Bytes == 0
        ? 0
        : (double)UsedSpace.Bytes / TotalSize.Bytes * 100;

    public string DisplayName => string.IsNullOrWhiteSpace(Label)
        ? $"{Name} ({RootPath})"
        : $"{Label} ({Name}: {RootPath})";
}
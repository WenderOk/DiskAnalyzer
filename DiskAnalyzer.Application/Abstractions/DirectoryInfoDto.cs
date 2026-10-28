namespace DiskAnalyzer.Application.Abstractions;

public sealed class DirectoryInfoDto
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required bool IsHidden { get; init; }
    public required bool IsSystem { get; init; }
    public required bool IsReparsePoint { get; init; }
}
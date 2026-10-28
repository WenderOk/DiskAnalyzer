namespace DiskAnalyzer.Application.Abstractions;

public sealed class FileInfoDto
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required string Extension { get; init; }
    public required string DirectoryPath { get; init; }
    public required long Size { get; init; }
    public required DateTime LastModified { get; init; }
    public required DateTime Created { get; init; }
    public required bool IsHidden { get; init; }
    public required bool IsSystem { get; init; }
    public required bool IsReparsePoint { get; init; }
}
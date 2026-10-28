namespace DiskAnalyzer.Domain.Models;

public sealed class IndexedFile
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required string Extension { get; init; }
    public required FileSize Size { get; init; }
    public required DateTime LastModified { get; init; }
    public string? ContentPreview { get; init; }
    public string? ContentHash { get; init; }
}
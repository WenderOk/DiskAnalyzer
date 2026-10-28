namespace DiskAnalyzer.Domain.Models;

public sealed class DuplicateGroup
{
    public required string ContentHash { get; init; }
    public required FileSize FileSize { get; init; }
    public required IReadOnlyList<FileEntry> Files { get; init; }

    public int Count => Files.Count;

    public FileSize WastedSpace => new(FileSize.Bytes * (Files.Count - 1));
}
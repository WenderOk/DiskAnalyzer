using DiskAnalyzer.Domain.Enums;

namespace DiskAnalyzer.Domain.Models;

public sealed class ScanResult
{
    public required DirectoryNode Root { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime CompletedAt { get; init; }
    public required ScanPhase Phase { get; init; }
    public List<string> Errors { get; init; } = new();

    public TimeSpan Duration => CompletedAt - StartedAt;
    public FileSize TotalSize => Root.TotalSize;
    public long TotalFiles => Root.FileCount;
    public long TotalFolders => Root.FolderCount;

    public IEnumerable<FileEntry> EnumerateAllFiles()
    {
        foreach (var node in Root.EnumerateSelfAndDescendants())
            foreach (var file in node.Files)
                yield return file;
    }

    public IReadOnlyList<FileEntry> GetTopLargestFiles(int count)
    {
        return EnumerateAllFiles()
            .OrderByDescending(f => f.Size.Bytes)
            .Take(count)
            .ToList();
    }

    public IReadOnlyDictionary<Enums.FileCategory, FileSize> GetCategoryBreakdown()
    {
        return EnumerateAllFiles()
            .GroupBy(f => f.Category)
            .ToDictionary(
                g => g.Key,
                g => new FileSize(g.Sum(f => f.Size.Bytes)));
    }
}
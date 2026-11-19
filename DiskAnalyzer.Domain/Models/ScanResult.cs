using DiskAnalyzer.Domain.Enums;

namespace DiskAnalyzer.Domain.Models;

public sealed class ScanResult
{
    public required DirectoryNode Root { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime CompletedAt { get; init; }
    public required ScanPhase Phase { get; init; }

    /// Плоский список найденных файлов. Нужен для поиска дубликатов и статистики
    public IReadOnlyList<FileEntry> Files { get; init; } = Array.Empty<FileEntry>();

    public IReadOnlyList<FileEntry> TopFiles { get; init; } = Array.Empty<FileEntry>();
    public List<string> Errors { get; init; } = new();

    public TimeSpan Duration => CompletedAt - StartedAt;
    public FileSize TotalSize => Root.TotalSize;
    public long TotalFiles => Root.FileCount;
    public long TotalFolders => Root.FolderCount;

    public IEnumerable<FileEntry> EnumerateAllFiles() => Files;

    public IReadOnlyList<FileEntry> GetTopLargestFiles(int count)
    {
        var heap = new PriorityQueue<FileEntry, long>();
        foreach (var file in Files)
        {
            heap.Enqueue(file, file.Size.Bytes);
            if (heap.Count > count)
                heap.Dequeue();
        }

        return heap.UnorderedItems
            .Select(x => x.Element)
            .OrderByDescending(f => f.Size.Bytes)
            .ToList();
    }

    public IReadOnlyDictionary<FileCategory, FileSize> GetCategoryBreakdown()
    {
        return Files
            .GroupBy(f => f.Category)
            .ToDictionary(
                g => g.Key,
                g => new FileSize(g.Sum(f => f.Size.Bytes)));
    }
}

using DiskAnalyzer.Domain.Filters;
using DiskAnalyzer.Domain.Models;
using DiskAnalyzer.Worker.Contracts;

namespace DiskAnalyzer.Worker;

internal static class PayloadMapper
{
    public static ScanFilter ToFilter(ScanFilterPayload? payload)
    {
        var defaults = ScanFilter.Default;
        return new ScanFilter
        {
            ExcludedPaths = new HashSet<string>(
                (IEnumerable<string>?)payload?.ExcludedPaths ?? defaults.ExcludedPaths,
                StringComparer.OrdinalIgnoreCase),
            ExcludedExtensions = new HashSet<string>(
                (IEnumerable<string>?)payload?.ExcludedExtensions ?? defaults.ExcludedExtensions,
                StringComparer.OrdinalIgnoreCase),
            ExcludedDirectoryNames = new HashSet<string>(
                (IEnumerable<string>?)payload?.ExcludedDirectoryNames ?? defaults.ExcludedDirectoryNames,
                StringComparer.OrdinalIgnoreCase),
            MinFileSize = new FileSize(payload?.MinFileSizeBytes ?? 0),
            IncludeHidden = payload?.IncludeHidden ?? defaults.IncludeHidden,
            IncludeSystem = payload?.IncludeSystem ?? defaults.IncludeSystem,
            MaxDepth = payload is null || payload.MaxDepth <= 0 ? int.MaxValue : payload.MaxDepth
        };
    }

    public static ScanProgressPayload ToPayload(ScanProgress progress) => new(
        progress.Phase.ToString(),
        progress.CurrentPath,
        progress.ProcessedFiles,
        progress.ProcessedFolders,
        progress.ProcessedSize.Bytes,
        progress.Percent,
        progress.Message,
        progress.SkippedUnreadableFiles);

    public static ScanResultPayload ToPayload(ScanResult result, string scanId) => new(
        scanId,
        Flatten(result.Root),
        (result.TopFiles.Count > 0 ? result.TopFiles : result.GetTopLargestFiles(50))
            .Select(ToPayload)
            .ToList(),
        result.Root.TotalSize.Bytes,
        result.Root.FileCount,
        result.Root.FolderCount,
        result.StartedAt,
        result.CompletedAt,
        result.Phase.ToString(),
        result.Errors);

    public static DuplicateGroupPayload ToPayload(DuplicateGroup group) => new(
        group.ContentHash,
        group.FileSize.Bytes,
        group.Files.Select(ToPayload).ToList());

    private static IReadOnlyList<DirectoryEntryPayload> Flatten(DirectoryNode root)
    {
        var entries = new List<DirectoryEntryPayload>();
        var stack = new Stack<(DirectoryNode Node, int ParentIndex)>();
        stack.Push((root, -1));

        while (stack.Count > 0)
        {
            var (node, parentIndex) = stack.Pop();
            var index = entries.Count;
            entries.Add(new DirectoryEntryPayload(
                parentIndex,
                node.Name,
                node.TotalSize.Bytes,
                node.FileCount,
                node.FolderCount));

            for (var i = node.Children.Count - 1; i >= 0; i--)
                stack.Push((node.Children[i], index));
        }

        return entries;
    }

    private static FileEntryPayload ToPayload(FileEntry file) => new(
        file.FullPath,
        file.Size.Bytes,
        file.LastModified,
        file.Created);
}

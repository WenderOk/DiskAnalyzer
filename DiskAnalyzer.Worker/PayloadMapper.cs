using DiskAnalyzer.Domain.Filters;
using DiskAnalyzer.Domain.Models;
using DiskAnalyzer.Worker.Contracts;

namespace DiskAnalyzer.Worker;

internal static class PayloadMapper
{
    public static ScanFilter ToFilter(ScanFilterPayload? payload) =>
        payload is null
            ? ScanFilter.Default
            : new ScanFilter
            {
                ExcludedPaths = new HashSet<string>(
                    payload.ExcludedPaths ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase),
                ExcludedExtensions = new HashSet<string>(
                    payload.ExcludedExtensions ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase),
                ExcludedDirectoryNames = new HashSet<string>(
                    payload.ExcludedDirectoryNames ?? Array.Empty<string>(),
                    StringComparer.OrdinalIgnoreCase),
                MinFileSize = new FileSize(payload.MinFileSizeBytes),
                IncludeHidden = payload.IncludeHidden,
                IncludeSystem = payload.IncludeSystem,
                MaxDepth = payload.MaxDepth
            };

    public static ScanProgressPayload ToPayload(ScanProgress progress) => new(
        progress.Phase.ToString(),
        progress.CurrentPath,
        progress.ProcessedFiles,
        progress.ProcessedFolders,
        progress.ProcessedSize.Bytes,
        progress.Percent,
        progress.Message);

    public static ScanResultPayload ToPayload(ScanResult result) => new(
        ToPayload(result.Root),
        result.StartedAt,
        result.CompletedAt,
        result.Phase.ToString(),
        result.Errors);

    public static DuplicateGroupPayload ToPayload(DuplicateGroup group) => new(
        group.ContentHash,
        group.FileSize.Bytes,
        group.Files.Select(ToPayload).ToList());

    private static DirectoryNodePayload ToPayload(DirectoryNode node) => new(
        node.FullPath,
        node.Name,
        node.TotalSize.Bytes,
        node.FileCount,
        node.FolderCount,
        node.Children.Select(ToPayload).ToList(),
        node.Files.Select(ToPayload).ToList());

    private static FileEntryPayload ToPayload(FileEntry file) => new(
        file.FullPath,
        file.Name,
        file.Extension,
        file.DirectoryPath,
        file.Size.Bytes,
        file.LastModified,
        file.Created);
}
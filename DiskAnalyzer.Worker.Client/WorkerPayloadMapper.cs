using DiskAnalyzer.Domain.Enums;
using DiskAnalyzer.Domain.Filters;
using DiskAnalyzer.Domain.Models;
using DiskAnalyzer.Worker.Contracts;

namespace DiskAnalyzer.Worker.Client;

public static class WorkerPayloadMapper
{
    public static ScanFilterPayload ToPayload(ScanFilter filter) => new(
        filter.ExcludedPaths.ToList(),
        filter.ExcludedExtensions.ToList(),
        filter.ExcludedDirectoryNames.ToList(),
        filter.MinFileSize.Bytes,
        filter.IncludeHidden,
        filter.IncludeSystem,
        filter.MaxDepth);

    public static ScanResult ToDomain(ScanResultPayload payload) => new()
    {
        Root = ToDomain(payload.Root, parent: null),
        StartedAt = payload.StartedAt,
        CompletedAt = payload.CompletedAt,
        Phase = Enum.Parse<ScanPhase>(payload.Phase, ignoreCase: true),
        Errors = payload.Errors.ToList()
    };

    public static DuplicateGroup ToDomain(DuplicateGroupPayload payload) => new()
    {
        ContentHash = payload.ContentHash,
        FileSize = new FileSize(payload.FileSizeBytes),
        Files = payload.Files.Select(ToDomain).ToList()
    };

    public static FileEntry ToDomain(FileEntryPayload payload) => new()
    {
        FullPath = payload.FullPath,
        Name = payload.Name,
        Extension = payload.Extension,
        DirectoryPath = payload.DirectoryPath,
        Size = new FileSize(payload.SizeBytes),
        LastModified = payload.LastModified,
        Created = payload.Created
    };

    public static DirectoryNode ToDomain(DirectoryNodePayload payload, DirectoryNode? parent)
    {
        var node = new DirectoryNode
        {
            FullPath = payload.FullPath,
            Name = payload.Name,
            Parent = parent
        };

        node.TotalSize = new FileSize(payload.TotalSizeBytes);
        node.FileCount = payload.FileCount;
        node.FolderCount = payload.FolderCount;

        foreach (var file in payload.Files)
            node.Files.Add(ToDomain(file));

        foreach (var child in payload.Children)
            node.Children.Add(ToDomain(child, node));

        return node;
    }
}
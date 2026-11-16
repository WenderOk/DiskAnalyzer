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

    public static DuplicateGroup ToDomain(DuplicateGroupPayload payload) => new()
    {
        ContentHash = payload.ContentHash,
        FileSize = new FileSize(payload.FileSizeBytes),
        Files = payload.Files.Select(ToDomain).ToList()
    };

    public static FileEntry ToDomain(FileEntryPayload payload) => new()
    {
        FullPath = payload.FullPath,
        Size = new FileSize(payload.SizeBytes),
        LastModified = payload.LastModified,
        Created = payload.Created
    };
}

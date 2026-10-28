namespace DiskAnalyzer.Worker.Contracts;

public sealed record ScanFilterPayload(
    IReadOnlyList<string>? ExcludedPaths,
    IReadOnlyList<string>? ExcludedExtensions,
    IReadOnlyList<string>? ExcludedDirectoryNames,
    long MinFileSizeBytes,
    bool IncludeHidden,
    bool IncludeSystem,
    int MaxDepth);

public sealed record ScanProgressPayload(
    string Phase,
    string CurrentPath,
    long ProcessedFiles,
    long ProcessedFolders,
    long ProcessedSizeBytes,
    double Percent,
    string? Message);

public sealed record FileEntryPayload(
    string FullPath,
    string Name,
    string Extension,
    string DirectoryPath,
    long SizeBytes,
    DateTime LastModified,
    DateTime Created);

public sealed record DirectoryNodePayload(
    string FullPath,
    string Name,
    long TotalSizeBytes,
    long FileCount,
    long FolderCount,
    IReadOnlyList<DirectoryNodePayload> Children,
    IReadOnlyList<FileEntryPayload> Files);

public sealed record ScanResultPayload(
    DirectoryNodePayload Root,
    DateTime StartedAt,
    DateTime CompletedAt,
    string Phase,
    IReadOnlyList<string> Errors);

public sealed record DuplicateGroupPayload(
    string ContentHash,
    long FileSizeBytes,
    IReadOnlyList<FileEntryPayload> Files);

public sealed record IndexResultPayload(
    long ProcessedFiles,
    long IndexedFiles);
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
    long SizeBytes,
    DateTime LastModified,
    DateTime Created);

/// <summary>
/// Плоская запись каталога. ParentIndex ссылается на запись-родителя
/// (для корня равен -1), поэтому дерево собирается на клиенте без рекурсии.
/// </summary>
public sealed record DirectoryEntryPayload(
    int ParentIndex,
    string Name,
    long TotalSizeBytes,
    long FileCount,
    long FolderCount);

public sealed record ScanResultPayload(
    string ScanId,
    IReadOnlyList<DirectoryEntryPayload> Directories,
    IReadOnlyList<FileEntryPayload> TopFiles,
    long TotalSizeBytes,
    long FileCount,
    long FolderCount,
    DateTime StartedAt,
    DateTime CompletedAt,
    string Phase,
    IReadOnlyList<string> Errors);

public sealed record DuplicateGroupPayload(
    string ContentHash,
    long FileSizeBytes,
    IReadOnlyList<FileEntryPayload> Files);

public sealed record DuplicateResultPayload(
    IReadOnlyList<DuplicateGroupPayload> Groups,
    long SkippedUnreadableFiles);

public sealed record IndexResultPayload(
    long ProcessedFiles,
    long IndexedFiles);

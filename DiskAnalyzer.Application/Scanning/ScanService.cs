using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Domain.Enums;
using DiskAnalyzer.Domain.Exceptions;
using DiskAnalyzer.Domain.Filters;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Scanning;

public sealed class ScanService
{
    private readonly IFileSystemService _fileSystem;

    public ScanService(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<ScanResult> ScanAsync(
        string rootPath,
        ScanFilter? filter = null,
        ScanOptions? options = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        filter ??= ScanFilter.Default;
        options ??= ScanOptions.Default;

        var normalizedRoot = NormalizePath(rootPath);
        if (!_fileSystem.DirectoryExists(normalizedRoot))
            throw new ScanFailedException($"Каталог '{normalizedRoot}' не существует.");

        var startedAt = DateTime.UtcNow;
        var context = new ScanContext(options.MaxDegreeOfParallelism, options.ExpectedTotalBytes, progress);
        var root = new DirectoryNode
        {
            FullPath = normalizedRoot,
            Name = GetDirectoryName(normalizedRoot)
        };

        var errors = new List<string>();
        ScanPhase phase;
        try
        {
            await ScanDirectoryAsync(root, filter, context, ct);
            phase = ScanPhase.Completed;
        }
        catch (OperationCanceledException)
        {
            phase = ScanPhase.Cancelled;
        }
        catch (Exception ex)
        {
            phase = ScanPhase.Failed;
            errors.Add(ex.Message);
        }

        context.TryReport(true);

        return new ScanResult
        {
            Root = root,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            Phase = phase,
            Errors = errors
        };
    }

    private async Task ScanDirectoryAsync(
        DirectoryNode node,
        ScanFilter filter,
        ScanContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        long ownSize = 0;
        foreach (var file in _fileSystem.EnumerateFiles(node.FullPath))
        {
            ct.ThrowIfCancellationRequested();
            if (file.IsReparsePoint) continue;

            var size = new FileSize(file.Size);
            if (filter.IsExcludedFile(file.FullPath, file.Extension, size, file.IsHidden, file.IsSystem))
                continue;

            node.Files.Add(new FileEntry
            {
                FullPath = file.FullPath,
                Name = file.Name,
                Extension = file.Extension,
                DirectoryPath = file.DirectoryPath,
                Size = size,
                LastModified = file.LastModified,
                Created = file.Created
            });
            ownSize += file.Size;
            context.ReportFile(file.FullPath, file.Size);
        }

        node.TotalSize = new FileSize(ownSize);
        node.FileCount = node.Files.Count;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = context.MaxDegreeOfParallelism,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(
            _fileSystem.EnumerateDirectories(node.FullPath),
            parallelOptions,
            async (directory, token) =>
            {
                if (directory.IsReparsePoint) return;
                if (filter.IsExcludedDirectory(
                        directory.FullPath, directory.Name, directory.IsHidden, directory.IsSystem))
                    return;

                var child = new DirectoryNode
                {
                    FullPath = directory.FullPath,
                    Name = directory.Name,
                    Parent = node
                };

                await ScanDirectoryAsync(child, filter, context, token);

                lock (node)
                {
                    node.Children.Add(child);
                    node.TotalSize += child.TotalSize;
                    node.FileCount += child.FileCount;
                    node.FolderCount += child.FolderCount + 1;
                }
            });
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Путь не может быть пустым.", nameof(path));
        return Path.GetFullPath(path);
    }

    private static string GetDirectoryName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
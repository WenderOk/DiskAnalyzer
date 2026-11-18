using System.Collections.Concurrent;
using System.Threading.Channels;
using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Domain.Enums;
using DiskAnalyzer.Domain.Exceptions;
using DiskAnalyzer.Domain.Filters;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Scanning;

public sealed class ScanService
{
    private const int MaxErrors = 100;
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

        // Предварительно считаем файлы, включая пустые, для определения процента.
        var totalFiles = await CountAsync(
            normalizedRoot, filter, options.MaxDegreeOfParallelism, progress, ct);

        var context = new ScanContext(options.MaxDegreeOfParallelism, totalFiles, progress);
        var files = new ConcurrentQueue<FileEntry>();
        var errors = new ConcurrentQueue<string>();

        var root = new DirectoryNode
        {
            FullPath = normalizedRoot,
            Name = GetDirectoryName(normalizedRoot)
        };

        var work = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
        var state = new QueueState { Pending = 1 };
        work.Writer.TryWrite(new WorkItem(root, 0));

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = context.MaxDegreeOfParallelism,
            CancellationToken = ct
        };

        context.SetPhase(ScanPhase.Enumerating);
        context.TryReport(true);

        ScanPhase phase;
        try
        {
            await Parallel.ForEachAsync(
                work.Reader.ReadAllAsync(ct),
                parallelOptions,
                (item, token) =>
                {
                    ProcessDirectory(item.Node, item.Depth, filter, context, work.Writer, state, files, errors, token);
                    return ValueTask.CompletedTask;
                });

            Aggregate(root);
            phase = ScanPhase.Completed;
        }
        catch (OperationCanceledException)
        {
            phase = ScanPhase.Cancelled;
            throw;
        }
        catch (Exception ex)
        {
            Aggregate(root);
            phase = ScanPhase.Failed;
            errors.Enqueue(ex.Message);
        }
        finally
        {
            work.Writer.TryComplete();
        }

        context.SetPhase(phase);
        context.TryReport(true);

        var allFiles = files.ToArray();
        return new ScanResult
        {
            Root = root,
            Files = allFiles,
            TopFiles = TakeTopLargest(allFiles, options.TopFilesCount),
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            Phase = phase,
            Errors = errors.Take(MaxErrors).ToList()
        };
    }

    private void ProcessDirectory(
        DirectoryNode node,
        int depth,
        ScanFilter filter,
        ScanContext context,
        ChannelWriter<WorkItem> writer,
        QueueState state,
        ConcurrentQueue<FileEntry> files,
        ConcurrentQueue<string> errors,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        long ownSize = 0;
        long ownFiles = 0;

        try
        {
            foreach (var entry in _fileSystem.EnumerateEntries(node.FullPath, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (entry.IsReparsePoint) continue;

                if (entry.IsDirectory)
                {
                    if (depth >= filter.MaxDepth) continue;
                    if (filter.IsExcludedDirectory(entry.FullPath, entry.Name, entry.IsHidden, entry.IsSystem))
                        continue;

                    var child = new DirectoryNode
                    {
                        FullPath = entry.FullPath,
                        Name = entry.Name,
                        Parent = node
                    };
                    node.Children.Add(child);

                    Interlocked.Increment(ref state.Pending);
                    writer.TryWrite(new WorkItem(child, depth + 1));
                }
                else
                {
                    var size = new FileSize(entry.Size);
                    if (filter.IsExcludedFile(entry.FullPath, entry.Extension, size, entry.IsHidden, entry.IsSystem))
                        continue;

                    files.Enqueue(new FileEntry
                    {
                        FullPath = entry.FullPath,
                        Size = size,
                        LastModified = entry.LastModified,
                        Created = entry.Created
                    });

                    ownFiles++;
                    ownSize += entry.Size;
                    context.ReportFile(entry.FullPath, entry.Size);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (errors.Count < MaxErrors)
                errors.Enqueue($"{node.FullPath}: {ex.Message}");
        }

        node.OwnSize = new FileSize(ownSize);
        node.OwnFileCount = ownFiles;
        context.ReportDirectory(node.FullPath);

        if (Interlocked.Decrement(ref state.Pending) == 0)
            writer.TryComplete();
    }

    private async Task<long> CountAsync(
        string rootPath,
        ScanFilter filter,
        int maxDegreeOfParallelism,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        long fileCount = 0;
        var work = Channel.CreateUnbounded<PathItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
        var state = new QueueState { Pending = 1 };
        work.Writer.TryWrite(new PathItem(rootPath, 0));

        var counting = new ScanContext(maxDegreeOfParallelism, 0, progress);
        counting.SetPhase(ScanPhase.Counting);
        counting.TryReport(true);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(
            work.Reader.ReadAllAsync(ct),
            options,
            (item, token) =>
            {
                CountDirectory(item, filter, work.Writer, state, ref fileCount, counting, token);
                return ValueTask.CompletedTask;
            });

        return Interlocked.Read(ref fileCount);
    }

    private void CountDirectory(
        PathItem item,
        ScanFilter filter,
        ChannelWriter<PathItem> writer,
        QueueState state,
        ref long fileCount,
        ScanContext counting,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            foreach (var entry in _fileSystem.EnumerateEntries(item.Path, ct))
            {
                ct.ThrowIfCancellationRequested();
                if (entry.IsReparsePoint) continue;

                if (entry.IsDirectory)
                {
                    if (item.Depth >= filter.MaxDepth) continue;
                    if (filter.IsExcludedDirectory(entry.FullPath, entry.Name, entry.IsHidden, entry.IsSystem))
                        continue;

                    Interlocked.Increment(ref state.Pending);
                    writer.TryWrite(new PathItem(entry.FullPath, item.Depth + 1));
                }
                else
                {
                    var size = new FileSize(entry.Size);
                    if (filter.IsExcludedFile(entry.FullPath, entry.Extension, size, entry.IsHidden, entry.IsSystem))
                        continue;

                    Interlocked.Increment(ref fileCount);
                    counting.ReportFile(entry.FullPath, entry.Size);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Недоступный каталог пропускаем; основной проход отразит это в Errors.
        }

        counting.ReportDirectory(item.Path);

        if (Interlocked.Decrement(ref state.Pending) == 0)
            writer.TryComplete();
    }

    private static void Aggregate(DirectoryNode node)
    {
        long size = node.OwnSize.Bytes;
        long files = node.OwnFileCount;
        long folders = 0;

        foreach (var child in node.Children)
        {
            Aggregate(child);
            size += child.TotalSize.Bytes;
            files += child.FileCount;
            folders += child.FolderCount + 1;
        }

        node.TotalSize = new FileSize(size);
        node.FileCount = files;
        node.FolderCount = folders;
    }

    private static IReadOnlyList<FileEntry> TakeTopLargest(IReadOnlyList<FileEntry> files, int count)
    {
        if (count <= 0 || files.Count == 0)
            return Array.Empty<FileEntry>();

        var heap = new PriorityQueue<FileEntry, long>();
        foreach (var file in files)
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

    private readonly record struct WorkItem(DirectoryNode Node, int Depth);
    private readonly record struct PathItem(string Path, int Depth);

    private sealed class QueueState
    {
        public int Pending;
    }
}

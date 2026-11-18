using System.Text;
using System.Threading.Channels;
using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Domain.Enums;
using DiskAnalyzer.Domain.Exceptions;
using DiskAnalyzer.Domain.Filters;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Indexing;

public sealed class IndexingService
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".csv", ".json", ".xml", ".html", ".css", ".js", ".ts",
        ".cs", ".java", ".py", ".rb", ".go", ".rs", ".cpp", ".h", ".c", ".hpp",
        ".yaml", ".yml", ".ini", ".cfg", ".conf", ".sql", ".sh", ".ps1", ".bat"
    };

    private readonly IFileSystemService _fileSystem;
    private readonly ISearchIndex _index;

    public IndexingService(IFileSystemService fileSystem, ISearchIndex index)
    {
        _fileSystem = fileSystem;
        _index = index;
    }

    public async Task<IndexingResult> IndexAsync(
        string rootPath,
        ScanFilter? filter = null,
        IndexingOptions? options = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        filter ??= ScanFilter.Default;
        options ??= IndexingOptions.Default;

        var normalizedRoot = Path.GetFullPath(rootPath);
        if (!_fileSystem.DirectoryExists(normalizedRoot))
            throw new ScanFailedException($"Каталог '{normalizedRoot}' не существует.");

        await _index.InitializeAsync(ct);

        var channel = Channel.CreateBounded<IndexedFile>(new BoundedChannelOptions(options.BatchSize * 2)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var producer = ProduceAsync(normalizedRoot, filter, options, channel.Writer, progress, cts.Token);
        var consumer = ConsumeAsync(channel.Reader, options, cts.Token);

        try
        {
            await Task.WhenAll(producer, consumer);
            return new IndexingResult(await producer, await consumer);
        }
        catch
        {
            cts.Cancel();
            try { await Task.WhenAll(producer, consumer); } catch { }
            throw;
        }
    }

    private async Task<long> ProduceAsync(
        string rootPath,
        ScanFilter filter,
        IndexingOptions options,
        ChannelWriter<IndexedFile> writer,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        long processed = 0;
        try
        {
            await TraverseAsync(rootPath, filter, options, async (file, token) =>
            {
                await writer.WriteAsync(file, token);

                var done = Interlocked.Increment(ref processed);
                if (done == 1 || done % 100 == 0)
                {
                    progress?.Report(new ScanProgress
                    {
                        Phase = ScanPhase.Enumerating,
                        CurrentPath = file.FullPath,
                        ProcessedFiles = done
                    });
                }
            }, ct);
        }
        finally
        {
            writer.Complete();
        }

        progress?.Report(new ScanProgress
        {
            Phase = ScanPhase.Finalizing,
            ProcessedFiles = processed
        });
        return processed;
    }

    private async Task<long> ConsumeAsync(
        ChannelReader<IndexedFile> reader,
        IndexingOptions options,
        CancellationToken ct)
    {
        var batch = new List<IndexedFile>(options.BatchSize);
        long indexed = 0;
        await foreach (var file in reader.ReadAllAsync(ct))
        {
            batch.Add(file);
            if (batch.Count >= options.BatchSize)
            {
                await _index.AddOrUpdateAsync(batch, ct);
                indexed += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _index.AddOrUpdateAsync(batch, ct);
            indexed += batch.Count;
        }

        return indexed;
    }

    private async Task TraverseAsync(
        string rootPath,
        ScanFilter filter,
        IndexingOptions options,
        Func<IndexedFile, CancellationToken, Task> onFile,
        CancellationToken ct)
    {
        var work = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
        var state = new QueueState { Pending = 1 };
        work.Writer.TryWrite(new WorkItem(rootPath, 0));

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(
            work.Reader.ReadAllAsync(ct),
            parallelOptions,
            async (item, token) =>
            {
                await ProcessDirectoryAsync(item, filter, options, onFile, work.Writer, state, token);
                if (Interlocked.Decrement(ref state.Pending) == 0)
                    work.Writer.TryComplete();
            });
    }

    private async Task ProcessDirectoryAsync(
        WorkItem item,
        ScanFilter filter,
        IndexingOptions options,
        Func<IndexedFile, CancellationToken, Task> onFile,
        ChannelWriter<WorkItem> writer,
        QueueState state,
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
                    writer.TryWrite(new WorkItem(entry.FullPath, item.Depth + 1));
                    continue;
                }

                var size = new FileSize(entry.Size);
                if (filter.IsExcludedFile(entry.FullPath, entry.Extension, size, entry.IsHidden, entry.IsSystem))
                    continue;

                string? preview = null;
                if (options.ExtractContentPreview && TextExtensions.Contains(entry.Extension))
                    preview = await ReadPreviewAsync(entry.FullPath, options.MaxPreviewChars, ct);

                await onFile(new IndexedFile
                {
                    FullPath = entry.FullPath,
                    Name = entry.Name,
                    Extension = entry.Extension,
                    Size = size,
                    LastModified = entry.LastModified,
                    ContentPreview = preview
                }, ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Каталог стал недоступен во время обхода — пропускаем его.
        }
    }

    private async Task<string?> ReadPreviewAsync(string path, int maxChars, CancellationToken ct)
    {
        try
        {
            await using var stream = _fileSystem.OpenRead(path);
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0) return null;

            var text = Encoding.UTF8.GetString(buffer, 0, read);
            return text.Length > maxChars ? text[..maxChars] : text;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private readonly record struct WorkItem(string Path, int Depth);

    private sealed class QueueState
    {
        public int Pending;
    }
}

public sealed record IndexingResult(long ProcessedFiles, long IndexedFiles);

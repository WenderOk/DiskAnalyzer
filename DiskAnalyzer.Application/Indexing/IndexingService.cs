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

    public async Task IndexAsync(
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
        }
        catch
        {
            cts.Cancel();
            try { await Task.WhenAll(producer, consumer); } catch { }
            throw;
        }
    }

    private async Task ProduceAsync(
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
                if (done % 100 == 0)
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
    }

    private async Task ConsumeAsync(
        ChannelReader<IndexedFile> reader,
        IndexingOptions options,
        CancellationToken ct)
    {
        var batch = new List<IndexedFile>(options.BatchSize);
        await foreach (var file in reader.ReadAllAsync(ct))
        {
            batch.Add(file);
            if (batch.Count >= options.BatchSize)
            {
                await _index.AddOrUpdateAsync(batch, ct);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            await _index.AddOrUpdateAsync(batch, ct);
    }

    private async Task TraverseAsync(
        string path,
        ScanFilter filter,
        IndexingOptions options,
        Func<IndexedFile, CancellationToken, Task> onFile,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var file in _fileSystem.EnumerateFiles(path))
        {
            ct.ThrowIfCancellationRequested();
            if (file.IsReparsePoint) continue;

            var size = new FileSize(file.Size);
            if (filter.IsExcludedFile(file.FullPath, file.Extension, size, file.IsHidden, file.IsSystem))
                continue;

            string? preview = null;
            if (options.ExtractContentPreview && TextExtensions.Contains(file.Extension))
                preview = await ReadPreviewAsync(file.FullPath, options.MaxPreviewChars, ct);

            await onFile(new IndexedFile
            {
                FullPath = file.FullPath,
                Name = file.Name,
                Extension = file.Extension,
                Size = size,
                LastModified = file.LastModified,
                ContentPreview = preview
            }, ct);
        }

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(
            _fileSystem.EnumerateDirectories(path),
            parallelOptions,
            async (directory, token) =>
            {
                if (directory.IsReparsePoint) return;
                if (filter.IsExcludedDirectory(
                        directory.FullPath, directory.Name, directory.IsHidden, directory.IsSystem))
                    return;

                await TraverseAsync(directory.FullPath, filter, options, onFile, token);
            });
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
}
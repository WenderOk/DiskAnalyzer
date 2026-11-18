using System.Collections.Concurrent;
using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Domain.Enums;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Duplicates;

public sealed class DuplicateFinderService(IHashService hashService)
{
    public async Task<DuplicateSearchResult> FindDuplicatesAsync(
        IEnumerable<FileEntry> files,
        DuplicateFinderOptions? options = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= DuplicateFinderOptions.Default;
        var skipped = 0L;
        // Кеш действует только в рамках текущего поиска: файлы могли измениться после сканирования.
        var fullHashes = new ConcurrentDictionary<string, string>();
        var candidates = files
            .Where(f => f.Size >= options.MinFileSize)
            .GroupBy(f => f.Size.Bytes)
            .Where(g => g.Count() > 1)
            .Select(g => g.ToList())
            .ToList();

        var partial = await HashAsync(candidates, partial: true);
        var full = await HashAsync(partial, partial: false);
        ct.ThrowIfCancellationRequested();
        var groups = full.Select(g => new DuplicateGroup
        {
            ContentHash = fullHashes[g[0].FullPath],
            FileSize = g[0].Size,
            Files = g.OrderBy(f => f.FullPath, StringComparer.Ordinal).ToList()
        }).OrderByDescending(g => g.WastedSpace.Bytes).ToList();

        progress?.Report(new ScanProgress
        {
            Phase = ScanPhase.Completed,
            Percent = 100,
            SkippedUnreadableFiles = skipped,
            Message = "Поиск завершён"
        });
        return new DuplicateSearchResult(groups, skipped);

        async Task<List<List<FileEntry>>> HashAsync(List<List<FileEntry>> input, bool partial)
        {
            var total = input.Sum(g => g.Count);
            var processed = 0;
            var buckets = new ConcurrentDictionary<string, List<FileEntry>>();
            var message = partial ? "Частичное хеширование" : "Полное хеширование";
            Report();
            await Parallel.ForEachAsync(input, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, options.MaxDegreeOfParallelism),
                CancellationToken = ct
            }, async (group, token) =>
            {
                foreach (var file in group)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        string hash;
                        if (!partial && fullHashes.TryGetValue(file.FullPath, out var cached))
                            hash = cached;
                        else if (!partial || file.Size.Bytes <= options.PartialHashSize)
                        {
                            hash = await hashService.ComputeFileHashAsync(file.FullPath, token);
                            fullHashes[file.FullPath] = hash;
                        }
                        else
                            hash = await hashService.ComputePartialFileHashAsync(
                                file.FullPath, options.PartialHashSize, token);

                        var bucket = buckets.GetOrAdd($"{file.Size.Bytes}:{hash}", _ => new());
                        lock (bucket) bucket.Add(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Interlocked.Increment(ref skipped);
                    }
                    // Ошибка чтения тоже считается обработанным файлом. Отмена не перехватывается.
                    if (Interlocked.Increment(ref processed) % 64 == 0) Report();
                }
            });
            Report();
            return buckets.Values.Where(g => g.Count > 1).ToList();

            void Report() => progress?.Report(new ScanProgress
            {
                Phase = ScanPhase.Hashing,
                Message = message,
                ProcessedFiles = Volatile.Read(ref processed),
                SkippedUnreadableFiles = Interlocked.Read(ref skipped),
                Percent = (partial ? 0 : 50) + (total > 0 ? 50.0 * Volatile.Read(ref processed) / total : 50)
            });
        }
    }
}

public sealed record DuplicateSearchResult(IReadOnlyList<DuplicateGroup> Groups, long SkippedUnreadableFiles);

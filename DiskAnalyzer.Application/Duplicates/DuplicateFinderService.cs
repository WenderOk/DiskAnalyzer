using System.Collections.Concurrent;
using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Domain.Enums;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Duplicates;

public sealed class DuplicateFinderService
{
    private readonly IHashService _hashService;

    public DuplicateFinderService(IHashService hashService)
    {
        _hashService = hashService;
    }

    public async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync(
        IEnumerable<FileEntry> files,
        DuplicateFinderOptions? options = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= DuplicateFinderOptions.Default;

        var candidates = files
            .Where(f => f.Size >= options.MinFileSize)
            .GroupBy(f => f.Size.Bytes)
            .Where(g => g.Count() > 1)
            .ToList();

        if (candidates.Count == 0)
            return Array.Empty<DuplicateGroup>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            CancellationToken = ct
        };

        var totalPhase1 = candidates.Sum(g => g.Count());
        var processedPhase1 = 0;
        var byPartialHash = new ConcurrentDictionary<string, List<FileEntry>>();

        Report(progress, "Частичное хеширование", 0, totalPhase1);

        await Parallel.ForEachAsync(candidates, parallelOptions, async (group, token) =>
        {
            foreach (var file in group)
            {
                token.ThrowIfCancellationRequested();

                string hash;
                if (file.Size.Bytes <= options.PartialHashSize)
                {
                    hash = await _hashService.ComputeFileHashAsync(file.FullPath, token);
                    file.ContentHash = hash;
                }
                else
                {
                    hash = await _hashService.ComputePartialFileHashAsync(
                        file.FullPath, options.PartialHashSize, token);
                }

                var key = $"{file.Size.Bytes}:{hash}";
                var list = byPartialHash.GetOrAdd(key, _ => new List<FileEntry>());
                lock (list) { list.Add(file); }

                var done = Interlocked.Increment(ref processedPhase1);
                if (done % 64 == 0)
                    Report(progress, "Частичное хеширование", done, totalPhase1);
            }
        });

        var stillCandidates = byPartialHash.Values.Where(l => l.Count > 1).ToList();
        if (stillCandidates.Count == 0)
            return Array.Empty<DuplicateGroup>();

        var totalPhase2 = stillCandidates.Sum(l => l.Count);
        var processedPhase2 = 0;
        var byFullHash = new ConcurrentDictionary<string, List<FileEntry>>();

        Report(progress, "Полное хеширование", 0, totalPhase2);

        await Parallel.ForEachAsync(stillCandidates, parallelOptions, async (group, token) =>
        {
            foreach (var file in group)
            {
                token.ThrowIfCancellationRequested();

                var fullHash = file.ContentHash
                    ?? await _hashService.ComputeFileHashAsync(file.FullPath, token);

                var list = byFullHash.GetOrAdd(fullHash, _ => new List<FileEntry>());
                lock (list) { list.Add(file); }

                var done = Interlocked.Increment(ref processedPhase2);
                if (done % 64 == 0)
                    Report(progress, "Полное хеширование", done, totalPhase2);
            }
        });

        var results = new List<DuplicateGroup>();
        foreach (var (hash, group) in byFullHash)
        {
            if (group.Count < 2) continue;
            results.Add(new DuplicateGroup
            {
                ContentHash = hash,
                FileSize = group[0].Size,
                Files = group
            });
        }

        return results;
    }

    private static void Report(IProgress<ScanProgress>? progress, string message, int done, int total)
    {
        progress?.Report(new ScanProgress
        {
            Phase = ScanPhase.Hashing,
            Message = message,
            ProcessedFiles = done,
            Percent = total > 0 ? (double)done / total * 100 : 0
        });
    }
}
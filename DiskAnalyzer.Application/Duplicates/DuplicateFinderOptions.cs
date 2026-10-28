using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Duplicates;

public sealed class DuplicateFinderOptions
{
    public FileSize MinFileSize { get; init; } = new(1024);
    public long PartialHashSize { get; init; } = 64 * 1024;
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    public static DuplicateFinderOptions Default { get; } = new();
}
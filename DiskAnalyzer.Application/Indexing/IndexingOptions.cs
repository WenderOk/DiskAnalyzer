namespace DiskAnalyzer.Application.Indexing;

public sealed class IndexingOptions
{
    public int BatchSize { get; init; } = 500;
    public int MaxPreviewChars { get; init; } = 500;
    public bool ExtractContentPreview { get; init; } = true;
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    public static IndexingOptions Default { get; } = new();
}
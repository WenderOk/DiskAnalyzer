namespace DiskAnalyzer.Application.Scanning;

public sealed class ScanOptions
{
    /// <summary>
    /// Для HDD больше 1–2 потоков обычно только замедляют скан, поэтому
    /// по умолчанию ограничиваемся четырьмя, а не ProcessorCount * 2.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = Math.Min(Environment.ProcessorCount, 4);
    public int TopFilesCount { get; init; } = 50;

    public static ScanOptions Default { get; } = new();
}

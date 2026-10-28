namespace DiskAnalyzer.Domain.Models;

public sealed class SearchQuery
{
    public string Text { get; init; } = string.Empty;
    public string? NamePattern { get; init; }
    public string? Extension { get; init; }
    public bool SearchContent { get; init; } = true;
    public bool CaseSensitive { get; init; }
    public FileSize MinSize { get; init; } = FileSize.Zero;
    public FileSize? MaxSize { get; init; }
    public DateTime? ModifiedAfter { get; init; }
    public DateTime? ModifiedBefore { get; init; }
    public int MaxResults { get; init; } = 1000;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Text)
        && string.IsNullOrWhiteSpace(NamePattern)
        && string.IsNullOrWhiteSpace(Extension);
}
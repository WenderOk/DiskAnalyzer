namespace DiskAnalyzer.Domain.Models;

public sealed class SearchResult
{
    public required IndexedFile File { get; init; }
    public string? MatchedSnippet { get; init; }
    public required int Score { get; init; }
}
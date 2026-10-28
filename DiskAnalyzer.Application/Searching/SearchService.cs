using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Searching;

public sealed class SearchService
{
    private readonly ISearchIndex _index;

    public SearchService(ISearchIndex index)
    {
        _index = index;
    }

    public Task InitializeAsync(CancellationToken ct = default) => _index.InitializeAsync(ct);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
        => _index.SearchAsync(query, ct);

    public Task<int> GetIndexedCountAsync(CancellationToken ct = default) => _index.GetCountAsync(ct);

    public Task ClearAsync(CancellationToken ct = default) => _index.ClearAsync(ct);
}
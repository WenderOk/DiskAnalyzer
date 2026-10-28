using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Abstractions;

public interface ISearchIndex : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken ct = default);
    Task AddOrUpdateAsync(IReadOnlyList<IndexedFile> files, CancellationToken ct = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default);
    Task<int> GetCountAsync(CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}
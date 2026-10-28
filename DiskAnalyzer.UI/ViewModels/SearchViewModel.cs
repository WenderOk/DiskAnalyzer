using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskAnalyzer.Application.Searching;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.UI.ViewModels;

public sealed partial class SearchViewModel : ViewModelBase
{
    private readonly SearchService _search;

    public SearchViewModel(SearchService search)
    {
        _search = search;
    }

    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string _extension = string.Empty;
    [ObservableProperty] private bool _searchContent = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _summary = string.Empty;

    public ObservableCollection<SearchResultViewModel> Results { get; } = new();

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy) return;

        Results.Clear();
        ErrorMessage = null;
        Summary = string.Empty;
        IsBusy = true;

        try
        {
            await _search.InitializeAsync();

            var query = new SearchQuery
            {
                Text = Text,
                Extension = string.IsNullOrWhiteSpace(Extension) ? null : Extension,
                SearchContent = SearchContent
            };

            var results = await _search.SearchAsync(query);
            foreach (var r in results)
                Results.Add(new SearchResultViewModel(r));

            Summary = $"Найдено: {Results.Count}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed class SearchResultViewModel
{
    public SearchResultViewModel(SearchResult result)
    {
        FileName = result.File.Name;
        FullPath = result.File.FullPath;
        Size = result.File.Size;
        LastModified = result.File.LastModified;
        Preview = result.File.ContentPreview;
    }

    public string FileName { get; }
    public string FullPath { get; }
    public FileSize Size { get; }
    public DateTime LastModified { get; }
    public string? Preview { get; }

    public string DisplaySize => Size.ToHumanReadable();
    public string DisplayDate => LastModified.ToString("yyyy-MM-dd HH:mm");
}
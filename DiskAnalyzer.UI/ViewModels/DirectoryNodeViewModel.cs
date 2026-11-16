using System.Collections.ObjectModel;
using DiskAnalyzer.Domain.Models;
using DiskAnalyzer.Worker.Contracts;

namespace DiskAnalyzer.UI.ViewModels;

/// <summary>
/// Ленивое представление каталога: дочерние VM создаются только при первом
/// обращении (то есть когда строка раскрывается в TreeDataGrid).
/// </summary>
public sealed class DirectoryNodeViewModel : ViewModelBase
{
    private readonly IReadOnlyList<DirectoryEntryPayload> _entries;
    private readonly IReadOnlyList<int>[] _childIndices;
    private readonly int _index;
    private ObservableCollection<DirectoryNodeViewModel>? _children;

    public DirectoryNodeViewModel(
        IReadOnlyList<DirectoryEntryPayload> entries,
        IReadOnlyList<int>[] childIndices,
        int index)
    {
        _entries = entries;
        _childIndices = childIndices;
        _index = index;
    }

    public string Name => _entries[_index].Name;
    public FileSize TotalSize => new(_entries[_index].TotalSizeBytes);
    public long FileCount => _entries[_index].FileCount;
    public long FolderCount => _entries[_index].FolderCount;

    public string DisplaySize => TotalSize.ToHumanReadable();
    public string DisplayFileCount => FileCount.ToString("N0");
    public string DisplayFolderCount => FolderCount.ToString("N0");

    public ObservableCollection<DirectoryNodeViewModel> Children => _children ??= new(
        _childIndices[_index]
            .OrderByDescending(i => _entries[i].TotalSizeBytes)
            .Select(i => new DirectoryNodeViewModel(_entries, _childIndices, i)));
}

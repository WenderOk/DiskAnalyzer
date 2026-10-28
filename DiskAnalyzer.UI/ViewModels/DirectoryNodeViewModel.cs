using System.Collections.ObjectModel;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.UI.ViewModels;

public sealed class DirectoryNodeViewModel : ViewModelBase
{
    public DirectoryNodeViewModel(DirectoryNode node)
    {
        Name = node.Name;
        FullPath = node.FullPath;
        TotalSize = node.TotalSize;
        FileCount = node.FileCount;
        FolderCount = node.FolderCount;
        Files = node.Files.Select(f => new FileEntryViewModel(f)).ToList();
        Children = new ObservableCollection<DirectoryNodeViewModel>(
            node.Children
                .OrderByDescending(c => c.TotalSize.Bytes)
                .Select(c => new DirectoryNodeViewModel(c)));
    }

    public string Name { get; }
    public string FullPath { get; }
    public FileSize TotalSize { get; }
    public long FileCount { get; }
    public long FolderCount { get; }
    public IReadOnlyList<FileEntryViewModel> Files { get; }
    public ObservableCollection<DirectoryNodeViewModel> Children { get; }

    public string DisplaySize => TotalSize.ToHumanReadable();
}
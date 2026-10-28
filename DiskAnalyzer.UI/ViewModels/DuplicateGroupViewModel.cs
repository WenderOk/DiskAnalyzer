using System.Collections.ObjectModel;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.UI.ViewModels;

public sealed class DuplicateGroupViewModel
{
    public DuplicateGroupViewModel(DuplicateGroup group)
    {
        ContentHash = group.ContentHash;
        FileSize = group.FileSize;
        Files = new ObservableCollection<FileEntryViewModel>(
            group.Files.Select(f => new FileEntryViewModel(f)));
        WastedBytes = group.WastedSpace.Bytes;
    }

    public string ContentHash { get; }
    public FileSize FileSize { get; }
    public ObservableCollection<FileEntryViewModel> Files { get; }
    public long WastedBytes { get; }

    public string DisplaySize => FileSize.ToHumanReadable();
    public string DisplayWasted => FileSize.FromBytes(WastedBytes).ToHumanReadable();
    public int Count => Files.Count;
}
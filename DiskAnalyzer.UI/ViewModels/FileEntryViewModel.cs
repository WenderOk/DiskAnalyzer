using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.UI.ViewModels;

public sealed class FileEntryViewModel
{
    public FileEntryViewModel(FileEntry file)
    {
        FullPath = file.FullPath;
        Name = file.Name;
        Size = file.Size;
        LastModified = file.LastModified;
    }

    public string FullPath { get; }
    public string Name { get; }
    public FileSize Size { get; }
    public DateTime LastModified { get; }

    public string DisplaySize => Size.ToHumanReadable();
    public string DisplayDate => LastModified.ToString("yyyy-MM-dd HH:mm");
}
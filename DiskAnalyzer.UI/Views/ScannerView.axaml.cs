using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DiskAnalyzer.UI.ViewModels;

namespace DiskAnalyzer.UI.Views;

public sealed partial class ScannerView : UserControl
{
    public ScannerView() => InitializeComponent();

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ScannerViewModel vm)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Выберите папку для сканирования",
                    AllowMultiple = false
                });

            if (folders.Count == 0)
                return;

            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                vm.ScanRootPath = path;
        }
        catch (Exception ex)
        {
            vm.ErrorMessage = ex.Message;
        }
    }
}

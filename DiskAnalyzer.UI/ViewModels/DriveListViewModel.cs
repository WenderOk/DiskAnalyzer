using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskAnalyzer.Application.Drives;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.UI.ViewModels;

public sealed partial class DriveListViewModel : ViewModelBase
{
    private readonly DriveCatalogService _driveCatalog;

    public event EventHandler<string>? ScanRequested;

    public DriveListViewModel(DriveCatalogService driveCatalog)
    {
        _driveCatalog = driveCatalog;
        Refresh();
    }

    [ObservableProperty]
    private ObservableCollection<DriveInfoModel> _drives = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private DriveInfoModel? _selectedDrive;

    public bool HasSelection => SelectedDrive is not null;

    [RelayCommand]
    private void Refresh()
    {
        var currentRoot = SelectedDrive?.RootPath;
        var drives = _driveCatalog.GetAllDrives();
        Drives = new ObservableCollection<DriveInfoModel>(drives);
        SelectedDrive = Drives.FirstOrDefault(d => d.RootPath == currentRoot) ?? Drives.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Scan()
    {
        if (SelectedDrive is null) return;
        ScanRequested?.Invoke(this, SelectedDrive.RootPath);
    }

    partial void OnSelectedDriveChanged(DriveInfoModel? value)
        => ScanCommand.NotifyCanExecuteChanged();
}
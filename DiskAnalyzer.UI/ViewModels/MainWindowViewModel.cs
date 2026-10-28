using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DiskAnalyzer.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(
        DriveListViewModel driveList,
        ScannerViewModel scanner,
        DuplicatesViewModel duplicates,
        SearchViewModel search,
        IndexingViewModel indexing)
    {
        DriveList = driveList;
        Scanner = scanner;
        Duplicates = duplicates;
        Search = search;
        Indexing = indexing;

        DriveList.ScanRequested += async (_, path) =>
        {
            SelectedTabIndex = 1;
            await Scanner.StartScanAsync(path);
        };

        Scanner.DuplicatesRequested += async (_, scanId) =>
        {
            SelectedTabIndex = 2;
            await Duplicates.StartAsync(scanId);
        };

        Scanner.IndexingRequested += async (_, path) =>
        {
            SelectedTabIndex = 4;
            await Indexing.StartAsync(path);
        };
    }

    public DriveListViewModel DriveList { get; }
    public ScannerViewModel Scanner { get; }
    public DuplicatesViewModel Duplicates { get; }
    public SearchViewModel Search { get; }
    public IndexingViewModel Indexing { get; }

    [ObservableProperty] private int _selectedTabIndex;

    [RelayCommand]
    private void SelectTab(string index)
    {
        if (int.TryParse(index, out var i))
            SelectedTabIndex = i;
    }
}
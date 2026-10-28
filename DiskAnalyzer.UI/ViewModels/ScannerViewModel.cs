using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskAnalyzer.Worker.Client;
using DiskAnalyzer.Worker.Contracts;

namespace DiskAnalyzer.UI.ViewModels;

public sealed partial class ScannerViewModel : ViewModelBase
{
    private readonly WorkerClient _worker;
    private CancellationTokenSource? _cts;

    public ScannerViewModel(WorkerClient worker)
    {
        _worker = worker;
    }

    public event EventHandler<string>? DuplicatesRequested;
    public event EventHandler<string>? IndexingRequested;

    [ObservableProperty] private string _scanRootPath = string.Empty;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private string _processedInfo = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string? _currentScanId;

    public ObservableCollection<FileEntryViewModel> TopFiles { get; } = new();
    public ObservableCollection<DirectoryNodeViewModel> RootNodes { get; } = new();

    [ObservableProperty] private HierarchicalTreeDataGridSource<DirectoryNodeViewModel>? _treeSource;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private bool CanCancel() => IsScanning;

    public async Task StartScanAsync(string path)
    {
        if (IsScanning) return;

        ScanRootPath = path;
        ErrorMessage = null;
        Summary = string.Empty;
        CurrentScanId = null;
        TopFiles.Clear();
        RootNodes.Clear();
        TreeSource = null;
        ProgressPercent = 0;

        IsScanning = true;
        _cts = new CancellationTokenSource();
        CancelCommand.NotifyCanExecuteChanged();

        try
        {
            if (!_worker.IsConnected)
                await _worker.StartAsync();

            var progress = new Progress<ScanProgressPayload>(p =>
            {
                ProgressPercent = p.Percent;
                CurrentPath = p.CurrentPath;
                ProcessedInfo = $"{p.ProcessedFiles:N0} файлов · {new DiskAnalyzer.Domain.Models.FileSize(p.ProcessedSizeBytes).ToHumanReadable()}";
            });

            var result = await _worker.ScanAsync(path, progress, ct: _cts.Token);

            CurrentScanId = BuildScanId(result);
            BuildTree(result);
            BuildTopFiles(result);
            Summary = BuildSummary(result);
        }
        catch (OperationCanceledException)
        {
            Summary = "Сканирование отменено.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanWorkWithResult))]
    private void FindDuplicates()
    {
        if (CurrentScanId is null) return;
        DuplicatesRequested?.Invoke(this, CurrentScanId);
    }

    [RelayCommand(CanExecute = nameof(CanWorkWithResult))]
    private void IndexResult()
    {
        if (string.IsNullOrWhiteSpace(ScanRootPath)) return;
        IndexingRequested?.Invoke(this, ScanRootPath);
    }

    private bool CanWorkWithResult() => CurrentScanId is not null && !IsScanning;

    private string BuildScanId(ScanResultPayload result)
        => Guid.NewGuid().ToString("N");

    private void BuildTree(ScanResultPayload payload)
    {
        var domain = WorkerPayloadMapper.ToDomain(payload);
        var root = new DirectoryNodeViewModel(domain.Root);
        RootNodes.Add(root);

        TreeSource = new HierarchicalTreeDataGridSource<DirectoryNodeViewModel>(RootNodes)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<DirectoryNodeViewModel>(
                    new TextColumn<DirectoryNodeViewModel, string>("Имя", x => x.Name),
                    x => x.Children),
                new TextColumn<DirectoryNodeViewModel, string>("Размер", x => x.DisplaySize, width: new GridLength(120)),
                new TextColumn<DirectoryNodeViewModel, string>("Файлов", x => x.FileCount.ToString("N0"), width: new GridLength(90)),
                new TextColumn<DirectoryNodeViewModel, string>("Папок", x => x.FolderCount.ToString("N0"), width: new GridLength(90))
            }
        };
    }

    private void BuildTopFiles(ScanResultPayload payload)
    {
        var top = payload.Root.Files
            .Concat(EnumerateFilesRecursive(payload.Root))
            .OrderByDescending(f => f.SizeBytes)
            .Take(50)
            .Select(f => new FileEntryViewModel(WorkerPayloadMapper.ToDomain(f)));

        foreach (var f in top)
            TopFiles.Add(f);
    }

    private static IEnumerable<FileEntryPayload> EnumerateFilesRecursive(DirectoryNodePayload node)
    {
        foreach (var child in node.Children)
        {
            foreach (var f in child.Files) yield return f;
            foreach (var f in EnumerateFilesRecursive(child)) yield return f;
        }
    }

    private static string BuildSummary(ScanResultPayload payload)
    {
        var totalSize = new DiskAnalyzer.Domain.Models.FileSize(payload.Root.TotalSizeBytes);
        var duration = payload.CompletedAt - payload.StartedAt;
        return $"Готово за {duration.TotalSeconds:0.##} с · {payload.Root.FileCount:N0} файлов · {payload.Root.FolderCount:N0} папок · {totalSize.ToHumanReadable()}";
    }

    partial void OnIsScanningChanged(bool value) => FindDuplicatesCommand.NotifyCanExecuteChanged();
    partial void OnCurrentScanIdChanged(string? value) => FindDuplicatesCommand.NotifyCanExecuteChanged();
}
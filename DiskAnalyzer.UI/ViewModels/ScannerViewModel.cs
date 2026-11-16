using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskAnalyzer.Domain.Enums;
using DiskAnalyzer.Domain.Filters;
using DiskAnalyzer.Domain.Models;
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
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _processedInfo = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string? _currentScanId;
    [ObservableProperty] private bool _includeHidden = true;
    [ObservableProperty] private bool _includeSystem;
    [ObservableProperty] private bool _hddMode;
    [ObservableProperty] private int _maxDepth;

    public ObservableCollection<FileEntryViewModel> TopFiles { get; } = new();
    public ObservableCollection<DirectoryNodeViewModel> RootNodes { get; } = new();

    [ObservableProperty] private HierarchicalTreeDataGridSource<DirectoryNodeViewModel>? _treeSource;

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScan()
    {
        if (!TryNormalizePath(out var path))
            return;

        await StartScanAsync(path);
    }

    private bool CanStartScan() => !IsScanning && !string.IsNullOrWhiteSpace(ScanRootPath);

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
        Status = "Подготовка…";
        CurrentScanId = null;
        TopFiles.Clear();
        RootNodes.Clear();
        TreeSource = null;
        ProgressPercent = 0;
        IsProgressIndeterminate = true;
        ProcessedInfo = string.Empty;

        IsScanning = true;
        _cts = new CancellationTokenSource();

        try
        {
            if (!_worker.IsConnected)
                await _worker.StartAsync();

            var progress = new Progress<ScanProgressPayload>(p =>
            {
                ProgressPercent = p.Percent;
                IsProgressIndeterminate = p.Percent <= 0;
                CurrentPath = p.CurrentPath;
                ProcessedInfo =
                    $"{p.ProcessedFiles:N0} файлов · {p.ProcessedFolders:N0} папок · " +
                    $"{new FileSize(p.ProcessedSizeBytes).ToHumanReadable()}";
                Status = string.IsNullOrWhiteSpace(p.Message) ? PhaseText(p.Phase) : p.Message;
            });

            var filter = new ScanFilter
            {
                ExcludedPaths = ScanFilter.Default.ExcludedPaths,
                ExcludedExtensions = ScanFilter.Default.ExcludedExtensions,
                ExcludedDirectoryNames = ScanFilter.Default.ExcludedDirectoryNames,
                IncludeHidden = IncludeHidden,
                IncludeSystem = IncludeSystem,
                MaxDepth = MaxDepth <= 0 ? int.MaxValue : MaxDepth
            };

            var result = await _worker.ScanAsync(
                path,
                progress,
                WorkerPayloadMapper.ToPayload(filter),
                maxDegreeOfParallelism: HddMode ? 1 : null,
                ct: _cts.Token);

            IsProgressIndeterminate = false;
            ProgressPercent = 100;
            Status = PhaseText(result.Phase);

            BuildTree(result);
            BuildTopFiles(result);

            var completed = Enum.TryParse<ScanPhase>(result.Phase, ignoreCase: true, out var phase) &&
                            phase == ScanPhase.Completed;
            CurrentScanId = completed ? result.ScanId : null;
            Summary = completed
                ? BuildSummary(result)
                : $"Сканирование не завершено: {PhaseText(result.Phase)}";

            if (!completed)
            {
                ErrorMessage = result.Errors.FirstOrDefault() ?? $"Сканирование: {PhaseText(result.Phase)}";
            }
            else if (result.Errors.Count > 0)
            {
                ErrorMessage = $"Предупреждений: {result.Errors.Count}. {result.Errors[0]}";
            }
        }
        catch (OperationCanceledException)
        {
            Summary = "Сканирование отменено.";
            Status = "Отменено";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Status = "Ошибка";
        }
        finally
        {
            IsScanning = false;
            IsProgressIndeterminate = false;
            _cts?.Dispose();
            _cts = null;
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

    private bool TryNormalizePath(out string path)
    {
        path = string.Empty;
        var raw = ScanRootPath.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            ErrorMessage = "Укажите путь к каталогу.";
            return false;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(raw);
            if (expanded == "~" || expanded.StartsWith("~/") || expanded.StartsWith("~\\"))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var tail = expanded.Length > 2 ? expanded[2..] : string.Empty;
                expanded = Path.Combine(home, tail);
            }

            var fullPath = Path.GetFullPath(expanded);
            if (File.Exists(fullPath))
                fullPath = Path.GetDirectoryName(fullPath)!;

            if (!Directory.Exists(fullPath))
            {
                ErrorMessage = $"Каталог не найден: {fullPath}";
                return false;
            }

            path = fullPath;
            ErrorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    private void BuildTree(ScanResultPayload payload)
    {
        var entries = payload.Directories;
        if (entries.Count == 0)
            return;

        var childIndices = new List<int>[entries.Count];
        for (var i = 0; i < childIndices.Length; i++)
            childIndices[i] = new List<int>();

        for (var i = 1; i < entries.Count; i++)
        {
            var parent = entries[i].ParentIndex;
            if (parent >= 0 && parent < entries.Count)
                childIndices[parent].Add(i);
        }

        RootNodes.Clear();
        RootNodes.Add(new DirectoryNodeViewModel(entries, childIndices, 0));

        TreeSource = new HierarchicalTreeDataGridSource<DirectoryNodeViewModel>(RootNodes)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<DirectoryNodeViewModel>(
                    new TextColumn<DirectoryNodeViewModel, string>("Имя", x => x.Name),
                    x => x.Children),
                new TextColumn<DirectoryNodeViewModel, string>(
                    "Размер", x => x.DisplaySize, width: new GridLength(120)),
                new TextColumn<DirectoryNodeViewModel, string>(
                    "Файлов", x => x.DisplayFileCount, width: new GridLength(90)),
                new TextColumn<DirectoryNodeViewModel, string>(
                    "Папок", x => x.DisplayFolderCount, width: new GridLength(90))
            }
        };
    }

    private void BuildTopFiles(ScanResultPayload payload)
    {
        foreach (var file in payload.TopFiles)
            TopFiles.Add(new FileEntryViewModel(WorkerPayloadMapper.ToDomain(file)));
    }

    private static string PhaseText(string phase) =>
        Enum.TryParse<ScanPhase>(phase, ignoreCase: true, out var value)
            ? PhaseText(value)
            : phase;

    private static string PhaseText(ScanPhase phase) => phase switch
    {
        ScanPhase.Enumerating => "Сканирование",
        ScanPhase.Hashing => "Хеширование",
        ScanPhase.Finalizing => "Завершение",
        ScanPhase.Completed => "Завершено",
        ScanPhase.Cancelled => "Отменено",
        ScanPhase.Failed => "Ошибка",
        _ => phase.ToString()
    };

    private static string BuildSummary(ScanResultPayload payload)
    {
        var totalSize = new FileSize(payload.TotalSizeBytes);
        var duration = payload.CompletedAt - payload.StartedAt;
        return $"Готово за {duration.TotalSeconds:0.##} с · {payload.FileCount:N0} файлов · " +
               $"{payload.FolderCount:N0} папок · {totalSize.ToHumanReadable()}";
    }

    partial void OnIsScanningChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
        StartScanCommand.NotifyCanExecuteChanged();
        FindDuplicatesCommand.NotifyCanExecuteChanged();
        IndexResultCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentScanIdChanged(string? value)
    {
        FindDuplicatesCommand.NotifyCanExecuteChanged();
        IndexResultCommand.NotifyCanExecuteChanged();
    }

    partial void OnScanRootPathChanged(string value) => StartScanCommand.NotifyCanExecuteChanged();
}

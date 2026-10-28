using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskAnalyzer.Domain.Models;
using DiskAnalyzer.Worker.Client;
using DiskAnalyzer.Worker.Contracts;

namespace DiskAnalyzer.UI.ViewModels;

public sealed partial class IndexingViewModel : ViewModelBase
{
    private readonly WorkerClient _worker;
    private CancellationTokenSource? _cts;

    public IndexingViewModel(WorkerClient worker)
    {
        _worker = worker;
    }

    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private string _processedInfo = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _summary = string.Empty;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private bool CanCancel() => IsBusy;

    public async Task StartAsync(string path)
    {
        if (IsBusy) return;

        Path = path;
        ErrorMessage = null;
        Summary = string.Empty;
        ProgressPercent = 0;

        IsBusy = true;
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
                ProcessedInfo = $"{p.ProcessedFiles:N0} файлов · {new FileSize(p.ProcessedSizeBytes).ToHumanReadable()}";
            });

            await _worker.IndexAsync(path, progress, ct: _cts.Token);
            Summary = "Индексация завершена. Можно переходить на вкладку «Поиск».";
        }
        catch (OperationCanceledException)
        {
            Summary = "Индексация отменена.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            CancelCommand.NotifyCanExecuteChanged();
        }
    }
}
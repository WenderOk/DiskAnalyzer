using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskAnalyzer.Domain.Models;
using DiskAnalyzer.Worker.Client;
using DiskAnalyzer.Worker.Contracts;

namespace DiskAnalyzer.UI.ViewModels;

public sealed partial class DuplicatesViewModel : ViewModelBase
{
    private readonly WorkerClient _worker;
    private CancellationTokenSource? _cts;

    public DuplicatesViewModel(WorkerClient worker)
    {
        _worker = worker;
    }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _summary = string.Empty;

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = new();

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private bool CanCancel() => IsBusy;

    public async Task StartAsync(string scanId)
    {
        if (IsBusy) return;

        Groups.Clear();
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
                Status = p.Message ?? string.Empty;
            });

            var payloads = await _worker.FindDuplicatesAsync(scanId, progress, ct: _cts.Token);

            long totalWasted = 0;
            foreach (var payload in payloads)
            {
                var domain = WorkerPayloadMapper.ToDomain(payload);
                var vm = new DuplicateGroupViewModel(domain);
                totalWasted += vm.WastedBytes;
                Groups.Add(vm);
            }

            Summary = $"Найдено групп: {Groups.Count} · можно освободить: {FileSize.FromBytes(totalWasted).ToHumanReadable()}";
        }
        catch (OperationCanceledException)
        {
            Summary = "Поиск отменён.";
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
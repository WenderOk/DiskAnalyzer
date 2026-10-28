using System.Threading.Channels;
using DiskAnalyzer.Worker.Contracts;

namespace DiskAnalyzer.Worker.Client;

public interface IPendingOperation
{
    void HandleResponse(WorkerResponse response);
    void Fail(Exception ex);
}

public sealed class WorkerOperation<T> : IDisposable, IPendingOperation
{
    private readonly Channel<ScanProgressPayload> _progress;
    private readonly TaskCompletionSource<T> _result;
    private readonly CancellationTokenSource _cts;
    private readonly Func<WorkerResponse, T> _extractResult;
    private readonly Action<string>? _cancelSender;

    private int _completed;
    private int _cancelSent;
    private int _disposed;

    internal WorkerOperation(
        string requestId,
        CancellationToken parentToken,
        Action<string>? cancelSender,
        Func<WorkerResponse, T> extractResult)
    {
        RequestId = requestId;
        _cancelSender = cancelSender;
        _extractResult = extractResult;

        _progress = Channel.CreateUnbounded<ScanProgressPayload>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);

        _cts.Token.Register(() =>
        {
            if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
                return;

            SendCancel();
            _progress.Writer.TryComplete();
            _result.TrySetCanceled();
        });
    }

    public string RequestId { get; }

    public IAsyncEnumerable<ScanProgressPayload> ReadProgressAsync(CancellationToken ct = default)
        => _progress.Reader.ReadAllAsync(ct);

    public Task<T> Result => _result.Task;

    public void Cancel() => _cts.Cancel();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _progress.Writer.TryComplete();
        _cts.Dispose();
    }

    void IPendingOperation.HandleResponse(WorkerResponse response) => HandleResponse(response);
    void IPendingOperation.Fail(Exception ex) => Fail(ex);

    internal void HandleResponse(WorkerResponse response)
    {
        switch (response.Type)
        {
            case WorkerMessageType.Progress:
                if (response.Progress is not null)
                    _progress.Writer.TryWrite(response.Progress);
                break;

            case WorkerMessageType.ScanCompleted:
            case WorkerMessageType.DedupCompleted:
            case WorkerMessageType.IndexCompleted:
            case WorkerMessageType.Pong:
                CompleteWith(() => _extractResult(response));
                break;

            case WorkerMessageType.Cancelled:
                CompleteCancelled();
                break;

            case WorkerMessageType.Error:
                Fail(new WorkerException(response.Error ?? "Ошибка Worker."));
                break;
        }
    }

    internal void Fail(Exception ex)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
            return;

        _progress.Writer.TryComplete();
        _result.TrySetException(ex);
    }

    private void CompleteWith(Func<T> extractor)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
            return;

        try
        {
            var value = extractor();
            _progress.Writer.TryComplete();
            _result.TrySetResult(value);
        }
        catch (Exception ex)
        {
            _progress.Writer.TryComplete();
            _result.TrySetException(ex);
        }
    }

    private void CompleteCancelled()
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
            return;

        _progress.Writer.TryComplete();
        _result.TrySetCanceled();
    }

    private void SendCancel()
    {
        if (Interlocked.Exchange(ref _cancelSent, 1) != 0)
            return;

        try { _cancelSender?.Invoke(RequestId); }
        catch { }
    }
}
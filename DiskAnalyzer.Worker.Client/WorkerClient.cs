using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading.Channels;
using DiskAnalyzer.Worker.Contracts;

namespace DiskAnalyzer.Worker.Client;

public sealed class WorkerClient : IAsyncDisposable
{
    private readonly WorkerClientOptions _options;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, IPendingOperation> _pending = new();
    private readonly CancellationTokenSource _lifetimeCts = new();

    private Process? _process;
    private NamedPipeClientStream? _pipe;
    private Task? _readerTask;
    private Task? _stderrTask;
    private volatile bool _started;

    public WorkerClient(WorkerClientOptions options)
    {
        _options = options;
    }

    public bool IsConnected => _started && _pipe?.IsConnected == true;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started)
            throw new InvalidOperationException("WorkerClient уже запущен.");

        var executable = ResolveWorkerExecutable();

        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true
        };

        _process = Process.Start(psi)
            ?? throw new WorkerException("Не удалось запустить процесс Worker.");

        _stderrTask = Task.Run(() => DrainStderrAsync(_process, _lifetimeCts.Token));

        var readyLine = await ReadReadyLineAsync(
            _process.StandardOutput, _options.ConnectTimeout, ct);

        if (readyLine is null || !readyLine.StartsWith(WorkerCommands.ReadyPrefix))
        {
            TryKill();
            throw new WorkerException("Worker не подтвердил готовность.");
        }

        var pipeName = readyLine[WorkerCommands.ReadyPrefix.Length..];

        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(_options.ConnectTimeout);

        try
        {
            await _pipe.ConnectAsync(connectCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill();
            throw new WorkerException("Таймаут подключения к Worker.");
        }

        _started = true;
        _readerTask = Task.Run(() => ReadLoopAsync(_pipe, _lifetimeCts.Token));
    }

    public async Task StopAsync()
    {
        if (!_started)
            return;

        _started = false;
        _lifetimeCts.Cancel();

        try { if (_pipe is not null) await _pipe.DisposeAsync(); }
        catch { }

        TryKill();

        if (_process is not null)
        {
            try { await _process.WaitForExitAsync().WaitAsync(_options.ShutdownTimeout); }
            catch { }
        }

        if (_readerTask is not null) { try { await _readerTask; } catch { } }
        if (_stderrTask is not null) { try { await _stderrTask; } catch { } }

        if (_pipe is not null) { await _pipe.DisposeAsync(); _pipe = null; }
        _process?.Dispose();
        _process = null;
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        using var op = CreateOperation(
            WorkerCommands.Ping,
            id => new WorkerRequest { RequestId = id, Command = WorkerCommands.Ping },
            _ => true);

        await op.Result.WaitAsync(ct);
    }

    public WorkerOperation<ScanResultPayload> Scan(
        string path,
        ScanFilterPayload? filter = null,
        int? maxDegreeOfParallelism = null)
    {
        return CreateOperation(
            WorkerCommands.Scan,
            id => new WorkerRequest
            {
                RequestId = id,
                Command = WorkerCommands.Scan,
                Path = path,
                Filter = filter,
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            },
            r => r.ScanResult ?? throw new WorkerException("Пустой ScanResult."));
    }

    public WorkerOperation<List<DuplicateGroupPayload>> FindDuplicates(
        string scanId,
        int? maxDegreeOfParallelism = null)
    {
        return CreateOperation(
            WorkerCommands.Dedup,
            id => new WorkerRequest
            {
                RequestId = id,
                Command = WorkerCommands.Dedup,
                ScanId = scanId,
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            },
            r => r.Duplicates ?? new List<DuplicateGroupPayload>());
    }

    public WorkerOperation<IndexResultPayload> Index(
        string path,
        ScanFilterPayload? filter = null,
        int? batchSize = null,
        bool? extractPreview = null,
        int? maxDegreeOfParallelism = null)
    {
        return CreateOperation(
            WorkerCommands.Index,
            id => new WorkerRequest
            {
                RequestId = id,
                Command = WorkerCommands.Index,
                Path = path,
                Filter = filter,
                BatchSize = batchSize,
                ExtractPreview = extractPreview,
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            },
            r => r.IndexResult ?? throw new WorkerException("Пустой IndexResult."));
    }

    public async Task<ScanResultPayload> ScanAsync(
        string path,
        IProgress<ScanProgressPayload>? progress = null,
        ScanFilterPayload? filter = null,
        int? maxDegreeOfParallelism = null,
        CancellationToken ct = default)
    {
        using var op = Scan(path, filter, maxDegreeOfParallelism);
        return await WaitWithProgressAsync(op, progress, ct);
    }

    public async Task<List<DuplicateGroupPayload>> FindDuplicatesAsync(
        string scanId,
        IProgress<ScanProgressPayload>? progress = null,
        int? maxDegreeOfParallelism = null,
        CancellationToken ct = default)
    {
        using var op = FindDuplicates(scanId, maxDegreeOfParallelism);
        return await WaitWithProgressAsync(op, progress, ct);
    }

    public async Task<IndexResultPayload> IndexAsync(
        string path,
        IProgress<ScanProgressPayload>? progress = null,
        ScanFilterPayload? filter = null,
        int? batchSize = null,
        bool? extractPreview = null,
        int? maxDegreeOfParallelism = null,
        CancellationToken ct = default)
    {
        using var op = Index(path, filter, batchSize, extractPreview, maxDegreeOfParallelism);
        return await WaitWithProgressAsync(op, progress, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _writeLock.Dispose();
        _lifetimeCts.Dispose();
    }

    private async Task<T> WaitWithProgressAsync<T>(
        WorkerOperation<T> op,
        IProgress<ScanProgressPayload>? progress,
        CancellationToken ct)
    {
        Task? pump = null;
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (progress is not null)
        {
            pump = Task.Run(async () =>
            {
                try
                {
                    await foreach (var p in op.ReadProgressAsync(readCts.Token))
                        progress.Report(p);
                }
                catch (OperationCanceledException) { }
                catch (ChannelClosedException) { }
            }, CancellationToken.None);
        }

        try
        {
            return await op.Result.WaitAsync(ct);
        }
        finally
        {
            readCts.Cancel();
            if (pump is not null) { try { await pump; } catch { } }
        }
    }

    private WorkerOperation<T> CreateOperation<T>(
        string command,
        Func<string, WorkerRequest> requestBuilder,
        Func<WorkerResponse, T> extractResult)
    {
        if (!IsConnected)
            throw new WorkerException("WorkerClient не подключён. Вызовите StartAsync().");

        var requestId = Guid.NewGuid().ToString("N");
        var operation = new WorkerOperation<T>(
            requestId,
            _lifetimeCts.Token,
            id => SendCancelSafe(id),
            extractResult);

        _pending[requestId] = operation;

        var request = requestBuilder(requestId);
        _ = SendRequestSafeAsync(request, requestId);

        return operation;
    }

    private async Task SendRequestSafeAsync(WorkerRequest request, string requestId)
    {
        try
        {
            await WriteFrameAsync(request, _lifetimeCts.Token);
        }
        catch (Exception ex)
        {
            if (_pending.TryRemove(requestId, out var pending))
                pending.Fail(new WorkerException("Не удалось отправить запрос Worker.", ex));
        }
    }

    private void SendCancelSafe(string requestId)
    {
        if (!IsConnected)
            return;

        var request = new WorkerRequest
        {
            RequestId = requestId,
            Command = WorkerCommands.Cancel
        };

        _ = WriteFrameAsync(request, CancellationToken.None)
            .ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task WriteFrameAsync(WorkerRequest request, CancellationToken ct)
    {
        var pipe = _pipe ?? throw new WorkerException("Канал Worker не инициализирован.");

        await _writeLock.WaitAsync(ct);
        try
        {
            await Frames.WriteAsync(pipe, request, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                WorkerResponse? response;
                try
                {
                    response = await Frames.ReadAsync<WorkerResponse>(stream, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { break; }
                catch (ObjectDisposedException) { break; }

                if (response is null)
                    break;

                if (_pending.TryGetValue(response.RequestId, out var operation))
                    operation.HandleResponse(response);
            }
        }
        catch (Exception ex)
        {
            FailAllPending(new WorkerException("Ошибка чтения канала Worker.", ex));
            return;
        }

        FailAllPending(new WorkerException("Соединение с Worker закрыто."));
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var requestId in _pending.Keys.ToList())
        {
            if (_pending.TryRemove(requestId, out var operation))
                operation.Fail(ex);
        }
    }

    private string ResolveWorkerExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_options.WorkerExecutablePath))
        {
            if (!File.Exists(_options.WorkerExecutablePath))
                throw new WorkerException($"Не найден Worker: {_options.WorkerExecutablePath}");
            return _options.WorkerExecutablePath;
        }

        var fileName = OperatingSystem.IsWindows()
            ? "DiskAnalyzer.Worker.exe"
            : "DiskAnalyzer.Worker";

        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
            throw new WorkerException($"Не найден Worker по умолчанию: {path}");

        return path;
    }

    private static async Task<string?> ReadReadyLineAsync(
        StreamReader reader, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try { return await reader.ReadLineAsync(cts.Token); }
        catch (OperationCanceledException) { return null; }
    }

    private static async Task DrainStderrAsync(Process process, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(ct);
                if (line is null) break;
                Debug.WriteLine($"[Worker stderr] {line}");
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    private void TryKill()
    {
        try { _process?.Kill(entireProcessTree: true); }
        catch { }
    }
}
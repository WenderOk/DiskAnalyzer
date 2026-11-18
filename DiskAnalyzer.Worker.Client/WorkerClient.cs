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
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ConcurrentDictionary<string, IPendingOperation> _pending = new();
    private readonly ConcurrentQueue<string> _stderrTail = new();

    private CancellationTokenSource _lifetimeCts = new();

    private Process? _process;
    private NamedPipeClientStream? _pipe;
    private Task? _readerTask;
    private Task? _stderrTask;
    private volatile bool _started;
    private volatile bool _stopping;
    private int _disposed;

    public WorkerClient(WorkerClientOptions options)
    {
        _options = options;
    }

    public bool IsConnected => _started && _pipe?.IsConnected == true;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_started)
                return;

            CleanupProcess();

            if (_lifetimeCts.IsCancellationRequested)
            {
                _lifetimeCts.Dispose();
                _lifetimeCts = new CancellationTokenSource();
            }

            while (_stderrTail.TryDequeue(out _)) { }

            var executable = ResolveWorkerExecutable();

            var psi = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };

            var process = Process.Start(psi)
                ?? throw new WorkerException("Не удалось запустить процесс Worker.");
            _process = process;
            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;

            _stderrTask = Task.Run(() => DrainStderrAsync(process, _lifetimeCts.Token));

            var readyLine = await ReadReadyLineAsync(
                process.StandardOutput, _options.ConnectTimeout, ct);

            if (readyLine is null || !readyLine.StartsWith(WorkerCommands.ReadyPrefix))
            {
                CleanupProcess();
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
                CleanupProcess();
                throw new WorkerException("Таймаут подключения к Worker.");
            }

            _started = true;
            _readerTask = Task.Run(() => ReadLoopAsync(_pipe, _lifetimeCts.Token));
        }
        catch
        {
            CleanupProcess();
            throw;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (!_started && _process is null && _pipe is null)
                return;

            _stopping = true;
            _started = false;

            try { _lifetimeCts.Cancel(); } catch { }

            if (_pipe is not null)
            {
                try { await _pipe.DisposeAsync(); } catch { }
                _pipe = null;
            }

            if (_process is not null)
            {
                try
                {
                    await _process.WaitForExitAsync().WaitAsync(_options.ShutdownTimeout);
                }
                catch
                {
                    TryKill();
                    try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
                }
            }

            if (_readerTask is not null) { try { await _readerTask; } catch { } }
            if (_stderrTask is not null) { try { await _stderrTask; } catch { } }

            CleanupProcess();
        }
        finally
        {
            _stopping = false;
            _lifecycleLock.Release();
        }
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        using var op = CreateOperation(
            WorkerCommands.Ping,
            id => new WorkerRequest { RequestId = id, Command = WorkerCommands.Ping },
            _ => true);

        using var reg = ct.Register(op.Cancel);
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

    public WorkerOperation<DuplicateResultPayload> FindDuplicates(
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
            r => r.DuplicateResult ?? throw new WorkerException("Пустой DuplicateResult."));
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

    public async Task<DuplicateResultPayload> FindDuplicatesAsync(
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await StopAsync();
        _writeLock.Dispose();
        _lifecycleLock.Dispose();
        _lifetimeCts.Dispose();
    }

    private async Task<T> WaitWithProgressAsync<T>(
        WorkerOperation<T> op,
        IProgress<ScanProgressPayload>? progress,
        CancellationToken ct)
    {
        Task? pump = null;
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var cancelReg = ct.Register(op.Cancel);

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
            SendCancelSafe,
            id => _pending.TryRemove(id, out _),
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
            // См. комментарий в WorkerHost: не рвём кадр посередине.
            await Frames.WriteAsync(pipe, request, CancellationToken.None);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(Stream stream, CancellationToken ct)
    {
        Exception? failure = null;
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
                catch (IOException ex)
                {
                    failure = new WorkerException($"Ошибка канала Worker: {ex.Message}", ex);
                    break;
                }
                catch (ObjectDisposedException) { break; }

                if (response is null)
                {
                    failure = new WorkerException("Соединение с Worker закрыто.");
                    break;
                }

                if (_pending.TryGetValue(response.RequestId, out var operation))
                    operation.HandleResponse(response);
            }
        }
        catch (Exception ex)
        {
            failure = new WorkerException("Ошибка чтения канала Worker.", ex);
        }

        if (ct.IsCancellationRequested)
            return;

        _started = false;
        FailAllPending(failure ?? new WorkerException("Соединение с Worker закрыто."));

        if (_pipe is not null)
        {
            try { await _pipe.DisposeAsync(); } catch { }
            _pipe = null;
        }
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var requestId in _pending.Keys.ToList())
        {
            if (_pending.TryRemove(requestId, out var operation))
                operation.Fail(ex);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_stopping || !ReferenceEquals(sender, _process))
            return;

        var exitCode = "неизвестен";
        try { if (_process is not null) exitCode = _process.ExitCode.ToString(); } catch { }

        var stderr = LastStderr();
        var message = $"Worker завершился неожиданно (код {exitCode}).";
        if (!string.IsNullOrWhiteSpace(stderr))
            message += $" Последние сообщения: {stderr}";

        _started = false;
        FailAllPending(new WorkerException(message));
    }

    private string? LastStderr()
    {
        if (_stderrTail.IsEmpty)
            return null;

        return string.Join(" | ", _stderrTail.TakeLast(10));
    }

    private void CleanupProcess()
    {
        _started = false;

        if (_process is not null)
        {
            try { _process.Exited -= OnProcessExited; } catch { }
            TryKill();
            _process.Dispose();
            _process = null;
        }

        if (_pipe is not null)
        {
            try { _pipe.Dispose(); } catch { }
            _pipe = null;
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

    private async Task DrainStderrAsync(Process process, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(ct);
                if (line is null) break;

                _stderrTail.Enqueue(line);
                while (_stderrTail.Count > 50)
                    _stderrTail.TryDequeue(out _);

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

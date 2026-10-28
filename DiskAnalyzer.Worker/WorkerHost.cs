using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Threading.Channels;
using DiskAnalyzer.Application.Duplicates;
using DiskAnalyzer.Application.Indexing;
using DiskAnalyzer.Application.Scanning;
using DiskAnalyzer.Domain.Models;
using DiskAnalyzer.Worker.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DiskAnalyzer.Worker;

public sealed class WorkerHost
{
    private const int MaxCachedScans = 8;

    private readonly IServiceProvider _services;

    public WorkerHost(IServiceProvider services)
    {
        _services = services;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var pipeName = $"disk-analyzer-{Environment.ProcessId}-{Guid.NewGuid():N}";

        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        Console.WriteLine($"{WorkerCommands.ReadyPrefix}{pipeName}");
        Console.Out.Flush();

        try
        {
            await server.WaitForConnectionAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ProcessClientAsync(server, ct);
    }

    private async Task ProcessClientAsync(Stream stream, CancellationToken ct)
    {
        var writeLock = new SemaphoreSlim(1, 1);
        var activeRequests = new ConcurrentDictionary<string, CancellationTokenSource>();
        var scanCache = new ConcurrentDictionary<string, ScanResult>();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = linkedCts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                WorkerRequest? request;
                try
                {
                    request = await Frames.ReadAsync<WorkerRequest>(stream, token);
                }
                catch (IOException) { break; }
                catch (OperationCanceledException) { break; }

                if (request is null)
                    break;

                if (request.Command == WorkerCommands.Cancel)
                {
                    if (activeRequests.TryRemove(request.RequestId, out var existing))
                    {
                        try { existing.Cancel(); } catch { }
                        existing.Dispose();
                    }
                    continue;
                }

                var requestCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                if (!activeRequests.TryAdd(request.RequestId, requestCts))
                {
                    requestCts.Dispose();
                    continue;
                }

                _ = Task.Run(() => HandleSafeAsync(
                    request, stream, writeLock, scanCache, requestCts, activeRequests));
            }
        }
        finally
        {
            foreach (var cts in activeRequests.Values)
            {
                try { cts.Cancel(); } catch { }
                cts.Dispose();
            }
        }
    }

    private async Task HandleSafeAsync(
        WorkerRequest request,
        Stream stream,
        SemaphoreSlim writeLock,
        ConcurrentDictionary<string, ScanResult> scanCache,
        CancellationTokenSource requestCts,
        ConcurrentDictionary<string, CancellationTokenSource> activeRequests)
    {
        try
        {
            await DispatchAsync(request, stream, writeLock, scanCache, requestCts.Token);
        }
        catch (OperationCanceledException)
        {
            await TrySendAsync(stream, writeLock, new WorkerResponse
            {
                RequestId = request.RequestId,
                Type = WorkerMessageType.Cancelled
            });
        }
        catch (Exception ex)
        {
            await TrySendAsync(stream, writeLock, new WorkerResponse
            {
                RequestId = request.RequestId,
                Type = WorkerMessageType.Error,
                Error = ex.Message
            });
        }
        finally
        {
            if (activeRequests.TryRemove(request.RequestId, out var removed))
                removed.Dispose();
        }
    }

    private async Task DispatchAsync(
        WorkerRequest request,
        Stream stream,
        SemaphoreSlim writeLock,
        ConcurrentDictionary<string, ScanResult> scanCache,
        CancellationToken ct)
    {
        switch (request.Command)
        {
            case WorkerCommands.Ping:
                await SendAsync(stream, writeLock, new WorkerResponse
                {
                    RequestId = request.RequestId,
                    Type = WorkerMessageType.Pong
                }, ct);
                break;

            case WorkerCommands.Scan:
                await HandleScanAsync(request, stream, writeLock, scanCache, ct);
                break;

            case WorkerCommands.Dedup:
                await HandleDedupAsync(request, stream, writeLock, scanCache, ct);
                break;

            case WorkerCommands.Index:
                await HandleIndexAsync(request, stream, writeLock, ct);
                break;

            default:
                await SendAsync(stream, writeLock, new WorkerResponse
                {
                    RequestId = request.RequestId,
                    Type = WorkerMessageType.Error,
                    Error = $"Неизвестная команда: '{request.Command}'"
                }, ct);
                break;
        }
    }

    private async Task HandleScanAsync(
        WorkerRequest request,
        Stream stream,
        SemaphoreSlim writeLock,
        ConcurrentDictionary<string, ScanResult> scanCache,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            throw new InvalidOperationException("Не указан путь для сканирования.");

        var scanService = _services.GetRequiredService<ScanService>();
        var filter = PayloadMapper.ToFilter(request.Filter);
        var options = new ScanOptions
        {
            MaxDegreeOfParallelism = request.MaxDegreeOfParallelism
                ?? Environment.ProcessorCount * 2
        };

        await using var pump = new ProgressPump<ScanProgress>(
            stream, writeLock,
            p => new WorkerResponse
            {
                RequestId = request.RequestId,
                Type = WorkerMessageType.Progress,
                Progress = PayloadMapper.ToPayload(p)
            });

        var result = await scanService.ScanAsync(request.Path, filter, options, pump.Progress, ct);
        scanCache[request.RequestId] = result;
        EvictOldScans(scanCache);

        await pump.CompleteAsync();
        await SendAsync(stream, writeLock, new WorkerResponse
        {
            RequestId = request.RequestId,
            Type = WorkerMessageType.ScanCompleted,
            ScanResult = PayloadMapper.ToPayload(result)
        }, ct);
    }

    private async Task HandleDedupAsync(
        WorkerRequest request,
        Stream stream,
        SemaphoreSlim writeLock,
        ConcurrentDictionary<string, ScanResult> scanCache,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ScanId))
            throw new InvalidOperationException("Не указан ScanId.");
        if (!scanCache.TryGetValue(request.ScanId, out var scan))
            throw new InvalidOperationException($"Сканирование '{request.ScanId}' не найдено.");

        var dedupService = _services.GetRequiredService<DuplicateFinderService>();
        var options = new DuplicateFinderOptions
        {
            MaxDegreeOfParallelism = request.MaxDegreeOfParallelism
                ?? Environment.ProcessorCount
        };

        await using var pump = new ProgressPump<ScanProgress>(
            stream, writeLock,
            p => new WorkerResponse
            {
                RequestId = request.RequestId,
                Type = WorkerMessageType.Progress,
                Progress = PayloadMapper.ToPayload(p)
            });

        var groups = await dedupService.FindDuplicatesAsync(
            scan.EnumerateAllFiles(), options, pump.Progress, ct);

        await pump.CompleteAsync();
        await SendAsync(stream, writeLock, new WorkerResponse
        {
            RequestId = request.RequestId,
            Type = WorkerMessageType.DedupCompleted,
            Duplicates = groups.Select(PayloadMapper.ToPayload).ToList()
        }, ct);
    }

    private async Task HandleIndexAsync(
        WorkerRequest request,
        Stream stream,
        SemaphoreSlim writeLock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
            throw new InvalidOperationException("Не указан путь для индексации.");

        var indexingService = _services.GetRequiredService<IndexingService>();
        var filter = PayloadMapper.ToFilter(request.Filter);
        var options = new IndexingOptions
        {
            BatchSize = request.BatchSize ?? 500,
            ExtractContentPreview = request.ExtractPreview ?? true,
            MaxDegreeOfParallelism = request.MaxDegreeOfParallelism
                ?? Environment.ProcessorCount
        };

        await using var pump = new ProgressPump<ScanProgress>(
            stream, writeLock,
            p => new WorkerResponse
            {
                RequestId = request.RequestId,
                Type = WorkerMessageType.Progress,
                Progress = PayloadMapper.ToPayload(p)
            });

        await indexingService.IndexAsync(request.Path, filter, options, pump.Progress, ct);

        await pump.CompleteAsync();
        await SendAsync(stream, writeLock, new WorkerResponse
        {
            RequestId = request.RequestId,
            Type = WorkerMessageType.IndexCompleted,
            IndexResult = new IndexResultPayload(0, 0)
        }, ct);
    }

    private static void EvictOldScans(ConcurrentDictionary<string, ScanResult> cache)
    {
        while (cache.Count > MaxCachedScans)
        {
            var firstKey = cache.Keys.FirstOrDefault();
            if (firstKey is null) break;
            cache.TryRemove(firstKey, out _);
        }
    }

    private static Task SendAsync(
        Stream stream, SemaphoreSlim writeLock, WorkerResponse response, CancellationToken ct)
        => WriteFrameAsync(stream, writeLock, response, ct);

    private static Task TrySendAsync(
        Stream stream, SemaphoreSlim writeLock, WorkerResponse response)
        => WriteFrameAsync(stream, writeLock, response, CancellationToken.None);

    private static async Task WriteFrameAsync(
        Stream stream, SemaphoreSlim writeLock, WorkerResponse response, CancellationToken ct)
    {
        await writeLock.WaitAsync(ct);
        try
        {
            await Frames.WriteAsync(stream, response, ct);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            writeLock.Release();
        }
    }

    private sealed class ProgressPump<T> : IAsyncDisposable
    {
        private readonly Channel<T> _channel;
        private readonly Task _pumpTask;
        private readonly IProgress<T> _progress;

        public IProgress<T> Progress => _progress;

        public ProgressPump(
            Stream stream,
            SemaphoreSlim writeLock,
            Func<T, WorkerResponse> mapper)
        {
            _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(32)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

            _progress = new Progress<T>(value => _channel.Writer.TryWrite(value));

            _pumpTask = Task.Run(async () =>
            {
                await foreach (var value in _channel.Reader.ReadAllAsync())
                {
                    await WriteFrameAsync(stream, writeLock, mapper(value), CancellationToken.None);
                }
            });
        }

        public async Task CompleteAsync()
        {
            _channel.Writer.TryComplete();
            try { await _pumpTask; } catch { }
        }

        public async ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            try { await _pumpTask; } catch { }
        }
    }
}
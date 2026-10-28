using DiskAnalyzer.Domain.Enums;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Scanning;

internal sealed class ScanContext
{
    private readonly IProgress<ScanProgress>? _progress;
    private readonly long _expectedTotalBytes;
    private readonly int _reportIntervalMs;

    private long _files;
    private long _bytes;
    private long _lastReportTick;
    private string _currentPath = string.Empty;

    public int MaxDegreeOfParallelism { get; }

    public ScanContext(int maxDegreeOfParallelism, long expectedTotalBytes,
        IProgress<ScanProgress>? progress, int reportIntervalMs = 100)
    {
        MaxDegreeOfParallelism = maxDegreeOfParallelism;
        _expectedTotalBytes = expectedTotalBytes;
        _progress = progress;
        _reportIntervalMs = reportIntervalMs;
    }

    public void ReportFile(string path, long size)
    {
        Interlocked.Increment(ref _files);
        Interlocked.Add(ref _bytes, size);
        Volatile.Write(ref _currentPath, path);
        TryReport(false);
    }

    public void TryReport(bool force)
    {
        if (_progress is null) return;

        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastReportTick);
        if (!force && now - last < _reportIntervalMs) return;
        if (Interlocked.CompareExchange(ref _lastReportTick, now, last) != last) return;

        var processedBytes = Interlocked.Read(ref _bytes);
        var percent = _expectedTotalBytes > 0
            ? Math.Min(100.0, (double)processedBytes / _expectedTotalBytes * 100)
            : 0;

        _progress.Report(new ScanProgress
        {
            Phase = ScanPhase.Enumerating,
            CurrentPath = Volatile.Read(ref _currentPath),
            ProcessedFiles = Interlocked.Read(ref _files),
            ProcessedSize = new FileSize(processedBytes),
            Percent = percent
        });
    }
}
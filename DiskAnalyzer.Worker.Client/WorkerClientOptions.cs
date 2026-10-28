namespace DiskAnalyzer.Worker.Client;

public sealed class WorkerClientOptions
{
    public string? WorkerExecutablePath { get; init; }
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
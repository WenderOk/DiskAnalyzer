namespace DiskAnalyzer.Worker.Contracts;

public static class WorkerCommands
{
    public const string ReadyPrefix = "READY:";

    public const string Ping = "ping";
    public const string Scan = "scan";
    public const string Dedup = "dedup";
    public const string Index = "index";
    public const string Cancel = "cancel";
}
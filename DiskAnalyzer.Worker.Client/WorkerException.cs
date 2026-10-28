namespace DiskAnalyzer.Worker.Client;

public sealed class WorkerException : Exception
{
    public WorkerException(string message) : base(message) { }
    public WorkerException(string message, Exception inner) : base(message, inner) { }
}
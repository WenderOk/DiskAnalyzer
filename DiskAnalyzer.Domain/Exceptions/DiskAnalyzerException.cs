namespace DiskAnalyzer.Domain.Exceptions;

public class DiskAnalyzerException : Exception
{
    public DiskAnalyzerException(string message) : base(message) { }
    public DiskAnalyzerException(string message, Exception inner) : base(message, inner) { }
}

public sealed class DriveNotReadyException : DiskAnalyzerException
{
    public string DriveName { get; }

    public DriveNotReadyException(string driveName)
        : base($"Диск '{driveName}' недоступен или не готов к работе.")
    {
        DriveName = driveName;
    }
}

public sealed class ScanFailedException : DiskAnalyzerException
{
    public ScanFailedException(string message) : base(message) { }
    public ScanFailedException(string message, Exception inner) : base(message, inner) { }
}

public sealed class SearchIndexException : DiskAnalyzerException
{
    public SearchIndexException(string message) : base(message) { }
    public SearchIndexException(string message, Exception inner) : base(message, inner) { }
}
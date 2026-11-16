using System.IO.Enumeration;
using DiskAnalyzer.Application.Abstractions;

namespace DiskAnalyzer.Infrastructure.FileSystem;

public sealed class FileSystemService : IFileSystemService
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Win32
    };

    public IEnumerable<FileSystemEntryDto> EnumerateEntries(string path, CancellationToken ct = default)
    {
        var entries = new FileSystemEnumerable<FileSystemEntryDto>(
            path,
            (ref FileSystemEntry entry) => Map(ref entry),
            Options);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public Stream OpenRead(string path) => new FileStream(
        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
        bufferSize: 81920, useAsync: true);

    private static FileSystemEntryDto Map(ref FileSystemEntry entry)
    {
        var attributes = entry.Attributes;
        return new FileSystemEntryDto(
            entry.ToFullPath(),
            entry.IsDirectory ? 0 : entry.Length,
            entry.LastWriteTimeUtc.UtcDateTime,
            entry.CreationTimeUtc.UtcDateTime,
            entry.IsDirectory,
            (attributes & FileAttributes.Hidden) != 0,
            (attributes & FileAttributes.System) != 0,
            (attributes & FileAttributes.ReparsePoint) != 0);
    }
}

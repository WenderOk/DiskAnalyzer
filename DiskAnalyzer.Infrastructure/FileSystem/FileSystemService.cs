using DiskAnalyzer.Application.Abstractions;

namespace DiskAnalyzer.Infrastructure.FileSystem;

public sealed class FileSystemService : IFileSystemService
{
    public IReadOnlyList<DirectoryInfoDto> EnumerateDirectories(string path)
    {
        var result = new List<DirectoryInfoDto>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                var dto = TryGetDirectoryInfo(dir);
                if (dto is not null)
                    result.Add(dto);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return result;
    }

    public IReadOnlyList<FileInfoDto> EnumerateFiles(string path)
    {
        var result = new List<FileInfoDto>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                var dto = TryGetFileInfo(file);
                if (dto is not null)
                    result.Add(dto);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return result;
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public Stream OpenRead(string path) => new FileStream(
        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
        bufferSize: 81920, useAsync: true);

    private static DirectoryInfoDto? TryGetDirectoryInfo(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            var attributes = info.Attributes;
            return new DirectoryInfoDto
            {
                FullPath = info.FullName,
                Name = info.Name,
                IsHidden = attributes.HasFlag(FileAttributes.Hidden),
                IsSystem = attributes.HasFlag(FileAttributes.System),
                IsReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint)
            };
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static FileInfoDto? TryGetFileInfo(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var attributes = info.Attributes;
            return new FileInfoDto
            {
                FullPath = info.FullName,
                Name = info.Name,
                Extension = info.Extension,
                DirectoryPath = info.DirectoryName ?? string.Empty,
                Size = info.Length,
                LastModified = info.LastWriteTimeUtc,
                Created = info.CreationTimeUtc,
                IsHidden = attributes.HasFlag(FileAttributes.Hidden),
                IsSystem = attributes.HasFlag(FileAttributes.System),
                IsReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint)
            };
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
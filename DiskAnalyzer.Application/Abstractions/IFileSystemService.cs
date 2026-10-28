namespace DiskAnalyzer.Application.Abstractions;

public interface IFileSystemService
{
    IReadOnlyList<DirectoryInfoDto> EnumerateDirectories(string path);
    IReadOnlyList<FileInfoDto> EnumerateFiles(string path);
    bool DirectoryExists(string path);
    bool FileExists(string path);
    Stream OpenRead(string path);
}
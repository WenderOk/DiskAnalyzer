namespace DiskAnalyzer.Application.Abstractions;

public interface IFileSystemService
{
    /// Лениво перечисляет содержимое одного каталога без рекурсии
    IEnumerable<FileSystemEntryDto> EnumerateEntries(string path, CancellationToken ct = default);

    bool DirectoryExists(string path);
    Stream OpenRead(string path);
}

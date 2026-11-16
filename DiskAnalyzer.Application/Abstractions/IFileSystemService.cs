namespace DiskAnalyzer.Application.Abstractions;

public interface IFileSystemService
{
    /// <summary>Лениво перечисляет содержимое одного каталога без рекурсии.</summary>
    IEnumerable<FileSystemEntryDto> EnumerateEntries(string path, CancellationToken ct = default);

    bool DirectoryExists(string path);
    Stream OpenRead(string path);
}

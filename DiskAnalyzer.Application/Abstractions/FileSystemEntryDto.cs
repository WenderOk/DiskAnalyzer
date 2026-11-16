namespace DiskAnalyzer.Application.Abstractions;

/// <summary>
/// Одна запись каталога (файл или подкаталог). Строковые поля вычисляются
/// из полного пути, чтобы не дублировать строки в памяти.
/// </summary>
public readonly record struct FileSystemEntryDto(
    string FullPath,
    long Size,
    DateTime LastModified,
    DateTime Created,
    bool IsDirectory,
    bool IsHidden,
    bool IsSystem,
    bool IsReparsePoint)
{
    public string Name => Path.GetFileName(FullPath);
    public string Extension => Path.GetExtension(FullPath);
}

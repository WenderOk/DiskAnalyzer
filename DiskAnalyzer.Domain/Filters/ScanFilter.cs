using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Domain.Filters;

public sealed class ScanFilter
{
    public HashSet<string> ExcludedPaths { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ExcludedExtensions { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ExcludedDirectoryNames { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public FileSize MinFileSize { get; init; } = FileSize.Zero;
    public bool IncludeHidden { get; init; }
    public bool IncludeSystem { get; init; }
    public int MaxDepth { get; init; } = int.MaxValue;

    public bool IsExcludedDirectory(string fullPath, string name, bool isHidden, bool isSystem)
    {
        if (!IncludeHidden && isHidden) return true;
        if (!IncludeSystem && isSystem) return true;
        if (ExcludedDirectoryNames.Contains(name)) return true;
        if (IsUnderExcludedPath(fullPath)) return true;
        return false;
    }

    public bool IsExcludedFile(string fullPath, string extension, FileSize size, bool isHidden, bool isSystem)
    {
        if (size < MinFileSize) return true;
        if (!IncludeHidden && isHidden) return true;
        if (!IncludeSystem && isSystem) return true;
        if (ExcludedExtensions.Contains(extension)) return true;
        if (IsUnderExcludedPath(fullPath)) return true;
        return false;
    }

    private bool IsUnderExcludedPath(string fullPath)
    {
        foreach (var path in ExcludedPaths)
        {
            if (fullPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static ScanFilter Default { get; } = new()
    {
        ExcludedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "bin", "obj", ".git", ".vs", ".idea", "$RECYCLE.BIN", "System Volume Information"
        }
    };
}
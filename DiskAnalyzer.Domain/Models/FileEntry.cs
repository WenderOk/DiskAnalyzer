using DiskAnalyzer.Domain.Enums;

namespace DiskAnalyzer.Domain.Models;

public sealed class FileEntry
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required string Extension { get; init; }
    public required string DirectoryPath { get; init; }
    public required FileSize Size { get; init; }
    public required DateTime LastModified { get; init; }
    public required DateTime Created { get; init; }

    public string? ContentHash { get; set; }

    public FileCategory Category => ExtensionToCategory(Extension);

    private static FileCategory ExtensionToCategory(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".txt" or ".md" or ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx"
                or ".ppt" or ".pptx" or ".rtf" or ".odt" or ".ods" => FileCategory.Document,

            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg"
                or ".webp" or ".tiff" or ".heic" => FileCategory.Image,

            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv"
                or ".flv" or ".webm" => FileCategory.Video,

            ".mp3" or ".wav" or ".flac" or ".aac"
                or ".ogg" or ".m4a" or ".wma" => FileCategory.Audio,

            ".zip" or ".rar" or ".7z" or ".tar"
                or ".gz" or ".bz2" or ".xz" => FileCategory.Archive,

            ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".h"
                or ".c" or ".go" or ".rs" or ".rb" or ".php" or ".xml"
                or ".json" or ".yaml" or ".yml" or ".html" or ".css" => FileCategory.Code,

            ".exe" or ".dll" or ".so" or ".dylib" or ".msi"
                or ".app" or ".deb" or ".rpm" or ".apk" => FileCategory.Executable,

            _ => FileCategory.Other
        };
}
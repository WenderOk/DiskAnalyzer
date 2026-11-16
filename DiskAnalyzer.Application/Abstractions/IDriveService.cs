using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Abstractions;

public interface IDriveService
{
    IReadOnlyList<DriveInfoModel> GetAllDrives(bool includeSystem = false);
    DriveInfoModel? GetDrive(string rootPath);

    /// <summary>Точки монтирования внутри каталога, в которые не нужно заходить при сканировании.</summary>
    IReadOnlyList<string> GetScanExcludedMountPoints(string rootPath);
}

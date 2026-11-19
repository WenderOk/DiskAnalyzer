using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Abstractions;

public interface IDriveService
{
    IReadOnlyList<DriveInfoModel> GetAllDrives(bool includeSystem = false);
    DriveInfoModel? GetDrive(string rootPath);

    /// Точки монтирования внутри каталога, в которые не нужно заходить при сканировании
    IReadOnlyList<string> GetScanExcludedMountPoints(string rootPath);
}

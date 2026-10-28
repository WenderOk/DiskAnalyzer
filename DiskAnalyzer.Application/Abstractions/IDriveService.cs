using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Abstractions;

public interface IDriveService
{
    IReadOnlyList<DriveInfoModel> GetAllDrives();
    DriveInfoModel? GetDrive(string rootPath);
}
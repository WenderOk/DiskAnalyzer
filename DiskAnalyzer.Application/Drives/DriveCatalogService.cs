using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Application.Drives;

public sealed class DriveCatalogService
{
    private readonly IDriveService _driveService;

    public DriveCatalogService(IDriveService driveService)
    {
        _driveService = driveService;
    }

    public IReadOnlyList<DriveInfoModel> GetAllDrives() => _driveService.GetAllDrives();

    public DriveInfoModel? GetDriveForPath(string path) => _driveService.GetDrive(path);
}
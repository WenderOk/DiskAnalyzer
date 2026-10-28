using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Domain.Enums;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Infrastructure.FileSystem;

public sealed class DriveService : IDriveService
{
    public IReadOnlyList<DriveInfoModel> GetAllDrives()
    {
        var result = new List<DriveInfoModel>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                result.Add(Map(drive));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
        return result;
    }

    public DriveInfoModel? GetDrive(string rootPath)
    {
        var root = Path.GetPathRoot(rootPath);
        if (string.IsNullOrEmpty(root))
            return null;

        try
        {
            return Map(new DriveInfo(root));
        }
        catch (ArgumentException) { return null; }
        catch (IOException) { return null; }
    }

    private static DriveInfoModel Map(DriveInfo drive)
    {
        var ready = drive.IsReady;
        return new DriveInfoModel
        {
            Name = drive.Name,
            RootPath = drive.RootDirectory.FullName,
            Label = ready ? drive.VolumeLabel : string.Empty,
            FileSystem = ready ? drive.DriveFormat : string.Empty,
            Kind = MapKind(drive.DriveType),
            TotalSize = new FileSize(ready ? drive.TotalSize : 0),
            FreeSpace = new FileSize(ready ? drive.TotalFreeSpace : 0),
            IsReady = ready
        };
    }

    private static DriveKind MapKind(DriveType type) => type switch
    {
        DriveType.Fixed => DriveKind.Fixed,
        DriveType.Removable => DriveKind.Removable,
        DriveType.Network => DriveKind.Network,
        DriveType.CDRom => DriveKind.Optical,
        DriveType.Ram => DriveKind.Ram,
        _ => DriveKind.Unknown
    };
}
using DiskAnalyzer.Application.Abstractions;
using DiskAnalyzer.Domain.Enums;
using DiskAnalyzer.Domain.Models;

namespace DiskAnalyzer.Infrastructure.FileSystem;

public sealed class DriveService : IDriveService
{
    private static readonly HashSet<string> VirtualFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "proc", "sysfs", "devtmpfs", "devpts", "tmpfs", "ramfs", "squashfs", "overlay",
        "overlayfs", "cgroup", "cgroup2fs", "securityfs", "debugfs", "tracefs", "configfs",
        "fusectl", "mqueue", "hugetlbfs", "pstore", "bpf_fs", "autofs", "binfmt_misc",
        "nsfs", "efivarfs", "selinuxfs", "rpc_pipefs", "nfsd", "anon_inodefs", "pipefs",
        "sockfs", "devfs", "fuse.gvfsd-fuse", "fuse.portal", "fuse.snapfuse", "fuse.lxcfs"
    };

    private static readonly string[] SystemMountPoints =
    {
        "/proc", "/sys", "/dev", "/run", "/snap", "/boot/efi",
        "/var/lib/docker", "/var/lib/containers", "/var/lib/snapd", "/var/snap",
        "/System/Volumes", "/private/var/vm"
    };

    public IReadOnlyList<DriveInfoModel> GetAllDrives(bool includeSystem = false)
    {
        var mounts = MountTable.ReadLinux();
        var byPath = new Dictionary<string, MountEntry>(StringComparer.Ordinal);
        foreach (var mount in mounts)
            byPath[NormalizeMount(mount.MountPoint)] = mount;

        var result = new List<DriveInfoModel>();
        var bySource = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                byPath.TryGetValue(NormalizeMount(drive.Name), out var mount);

                if (!includeSystem && ShouldHide(drive, mount))
                    continue;

                var model = Map(drive, mount);

                // В обычном списке показываем один пункт на устройство: btrfs-сабволюмы
                // и bind-монтирования одного диска пользователю только мешают.
                var source = mount?.Source;
                if (!includeSystem && !string.IsNullOrEmpty(source) &&
                    bySource.TryGetValue(source, out var existingIndex))
                {
                    if (IsBetterRoot(model.RootPath, result[existingIndex].RootPath))
                        result[existingIndex] = model;
                    continue;
                }

                if (!string.IsNullOrEmpty(source))
                    bySource[source] = result.Count;
                result.Add(model);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }

        return result;
    }

    public IReadOnlyList<string> GetScanExcludedMountPoints(string rootPath)
    {
        if (!OperatingSystem.IsLinux())
            return Array.Empty<string>();

        string normalizedRoot;
        try { normalizedRoot = NormalizeMount(Path.GetFullPath(rootPath)); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return Array.Empty<string>();
        }

        var mounts = MountTable.ReadLinux();
        var rootMount = mounts
            .Where(m => IsUnder(normalizedRoot, m.MountPoint))
            .OrderByDescending(m => m.MountPoint.Length)
            .FirstOrDefault();
        var rootSource = rootMount?.Source;

        var result = new List<string>();

        foreach (var mount in mounts)
        {
            var mountPoint = NormalizeMount(mount.MountPoint);
            if (mountPoint == normalizedRoot || !IsUnder(mountPoint, normalizedRoot))
                continue;

            var isVirtual = VirtualFileSystems.Contains(mount.FileSystem) ||
                            IsSystemMountPoint(mountPoint) ||
                            mount.Source.StartsWith("/dev/loop", StringComparison.Ordinal) ||
                            mount.Source.StartsWith("/dev/zram", StringComparison.Ordinal);

            // По умолчанию не уходим на другое устройство (другой диск/раздел):
            // каждый диск и так доступен отдельным пунктом в списке.
            var isForeignDevice = !string.IsNullOrEmpty(rootSource) &&
                                  !string.IsNullOrEmpty(mount.Source) &&
                                  !string.Equals(rootSource, mount.Source, StringComparison.Ordinal);

            // Корень не трогаем: даже overlay/tmpfs в контейнере — это данные пользователя.
            if ((isVirtual || isForeignDevice) && mountPoint != "/")
                result.Add(mount.MountPoint);
        }

        return result;
    }

    public DriveInfoModel? GetDrive(string rootPath)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var fullPath = NormalizeMount(Path.GetFullPath(rootPath));
                var mount = MountTable.ReadLinux()
                    .Where(m => IsUnder(fullPath, m.MountPoint))
                    .OrderByDescending(m => m.MountPoint.Length)
                    .FirstOrDefault();

                if (mount is not null)
                    return Map(new DriveInfo(mount.MountPoint), mount);
            }

            var root = Path.GetPathRoot(rootPath);
            if (string.IsNullOrEmpty(root))
                return null;

            return Map(new DriveInfo(root), null);
        }
        catch (ArgumentException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static bool ShouldHide(DriveInfo drive, MountEntry? mount)
    {
        var mountPoint = NormalizeMount(drive.Name);
        var isRoot = mountPoint == "/";

        if (!drive.IsReady)
            return true;

        if (!OperatingSystem.IsWindows())
        {
            var fileSystem = mount?.FileSystem ?? SafeFormat(drive);
            var source = mount?.Source ?? string.Empty;

            if (!isRoot && VirtualFileSystems.Contains(fileSystem))
                return true;

            if (source.StartsWith("/dev/loop", StringComparison.Ordinal) ||
                source.StartsWith("/dev/zram", StringComparison.Ordinal))
                return true;

            if (!isRoot && IsSystemMountPoint(mountPoint))
                return true;
        }

        return false;
    }

    private static bool IsSystemMountPoint(string mountPoint) =>
        SystemMountPoints.Any(p =>
            mountPoint.Equals(p, StringComparison.Ordinal) ||
            mountPoint.StartsWith(p + "/", StringComparison.Ordinal));

    private static bool IsBetterRoot(string candidate, string existing)
    {
        if (candidate == "/") return true;
        if (existing == "/") return false;
        return candidate.Length < existing.Length;
    }

    private static bool IsUnder(string fullPath, string mountPoint)
    {
        var normalized = NormalizeMount(mountPoint);
        if (normalized == "/")
            return true;

        return fullPath.Equals(normalized, StringComparison.Ordinal) ||
               fullPath.StartsWith(normalized + "/", StringComparison.Ordinal);
    }

    private static string NormalizeMount(string path)
    {
        if (path.Length > 1)
            path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path.Length == 0 ? "/" : path;
    }

    private static string SafeFormat(DriveInfo drive)
    {
        try { return drive.DriveFormat; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static DriveInfoModel Map(DriveInfo drive, MountEntry? mount)
    {
        var ready = drive.IsReady;
        var fileSystem = mount?.FileSystem;
        if (string.IsNullOrEmpty(fileSystem))
            fileSystem = ready ? SafeFormat(drive) : string.Empty;

        return new DriveInfoModel
        {
            Name = drive.Name,
            RootPath = drive.RootDirectory.FullName,
            Label = ready ? SafeLabel(drive) : string.Empty,
            FileSystem = fileSystem,
            Kind = MapKind(drive.DriveType),
            TotalSize = new FileSize(ready ? drive.TotalSize : 0),
            FreeSpace = new FileSize(ready ? drive.TotalFreeSpace : 0),
            IsReady = ready
        };
    }

    private static string SafeLabel(DriveInfo drive)
    {
        try { return drive.VolumeLabel; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return string.Empty;
        }
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

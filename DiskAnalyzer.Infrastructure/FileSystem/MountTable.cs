namespace DiskAnalyzer.Infrastructure.FileSystem;

internal sealed record MountEntry(string MountPoint, string FileSystem, string Source, bool ReadOnly);

/// <summary>
/// Минимальный разбор /proc/self/mountinfo. Нужен потому, что DriveInfo на Linux
/// не сообщает источник (устройство) и не позволяет отличить реальный диск
/// от squashfs/overlay/proc/tmpfs.
/// </summary>
internal static class MountTable
{
    public static IReadOnlyList<MountEntry> ReadLinux()
    {
        var result = new List<MountEntry>();
        if (!OperatingSystem.IsLinux())
            return result;

        try
        {
            foreach (var line in File.ReadLines("/proc/self/mountinfo"))
            {
                var parts = line.Split(' ');
                var separator = Array.IndexOf(parts, "-");
                if (separator < 0 || separator + 2 >= parts.Length || parts.Length < 6)
                    continue;

                var mountPoint = Unescape(parts[4]);
                var options = parts[5];
                var fileSystem = parts[separator + 1];
                var source = Unescape(parts[separator + 2]);

                result.Add(new MountEntry(
                    mountPoint,
                    fileSystem,
                    source,
                    options.Split(',').Contains("ro", StringComparer.Ordinal)));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return result;
    }

    private static string Unescape(string value) => value
        .Replace("\\040", " ", StringComparison.Ordinal)
        .Replace("\\011", "\t", StringComparison.Ordinal)
        .Replace("\\012", "\n", StringComparison.Ordinal)
        .Replace("\\134", "\\", StringComparison.Ordinal);
}

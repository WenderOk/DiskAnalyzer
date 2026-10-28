namespace DiskAnalyzer.Domain.Models;

public readonly struct FileSize : IComparable<FileSize>, IEquatable<FileSize>
{
    public long Bytes { get; }

    public FileSize(long bytes)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes), "Размер не может быть отрицательным.");
        Bytes = bytes;
    }

    public static FileSize Zero => new(0);
    public static FileSize FromBytes(long bytes) => new(bytes);
    public static FileSize FromKilobytes(double kb) => new((long)(kb * 1024));
    public static FileSize FromMegabytes(double mb) => new((long)(mb * 1024 * 1024));
    public static FileSize FromGigabytes(double gb) => new((long)(gb * 1024L * 1024 * 1024));

    public double Kilobytes => Bytes / 1024.0;
    public double Megabytes => Bytes / (1024.0 * 1024);
    public double Gigabytes => Bytes / (1024.0 * 1024 * 1024);

    public int CompareTo(FileSize other) => Bytes.CompareTo(other.Bytes);
    public bool Equals(FileSize other) => Bytes == other.Bytes;
    public override bool Equals(object? obj) => obj is FileSize other && Equals(other);
    public override int GetHashCode() => Bytes.GetHashCode();

    public static FileSize operator +(FileSize a, FileSize b) => new(a.Bytes + b.Bytes);
    public static FileSize operator -(FileSize a, FileSize b) => new(a.Bytes - b.Bytes);
    public static bool operator >(FileSize a, FileSize b) => a.Bytes > b.Bytes;
    public static bool operator <(FileSize a, FileSize b) => a.Bytes < b.Bytes;
    public static bool operator >=(FileSize a, FileSize b) => a.Bytes >= b.Bytes;
    public static bool operator <=(FileSize a, FileSize b) => a.Bytes <= b.Bytes;
    public static bool operator ==(FileSize a, FileSize b) => a.Bytes == b.Bytes;
    public static bool operator !=(FileSize a, FileSize b) => a.Bytes != b.Bytes;

    public string ToHumanReadable()
    {
        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        double value = Bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    public override string ToString() => ToHumanReadable();
}
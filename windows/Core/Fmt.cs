using System.Globalization;

namespace DiskWatch.Core;

public static class Fmt
{
    private static readonly string[] Units = { "bytes", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>Decimal (1000-based) sizes, matching the macOS version and Windows Explorer's rounding style.</summary>
    public static string Bytes(long v)
    {
        if (v < 1000) return v == 1 ? "1 byte" : $"{v} bytes";
        double d = v;
        int u = 0;
        while (d >= 1000 && u < Units.Length - 1) { d /= 1000; u++; }
        string num = u <= 1 ? Math.Round(d).ToString("0", CultureInfo.CurrentCulture)
                   : d >= 100 ? d.ToString("0.#", CultureInfo.CurrentCulture)
                   : d.ToString("0.##", CultureInfo.CurrentCulture);
        return $"{num} {Units[u]}";
    }

    public static string Count(long n) => n.ToString("N0", CultureInfo.CurrentCulture);
    public static string Percent(double f) => f >= 0.9995 ? "100%" : (f * 100).ToString("0.0", CultureInfo.CurrentCulture) + "%";

    public static string Date(long ticks) => ticks <= 0 ? "—" :
        new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("MMM d, yyyy 'at' h:mm tt", CultureInfo.CurrentCulture);

    public static string ShortDate(long ticks) => ticks <= 0 ? "—" :
        new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("M/d/yy", CultureInfo.CurrentCulture);

    public static string ShortDateTime(long ticks) => ticks <= 0 ? "—" :
        new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("M/d/yy h:mm tt", CultureInfo.CurrentCulture);

    public static string Duration(double s) => s < 60 ? s.ToString("0.0", CultureInfo.CurrentCulture) + " s" : $"{(int)s / 60} min {(int)s % 60:00} s";

    public static string Attributes(FileAttributes a)
    {
        var parts = new List<string>();
        if (a.HasFlag(FileAttributes.ReadOnly)) parts.Add("read-only");
        if (a.HasFlag(FileAttributes.Hidden)) parts.Add("hidden");
        if (a.HasFlag(FileAttributes.System)) parts.Add("system");
        if (a.HasFlag(FileAttributes.Compressed)) parts.Add("compressed");
        if (a.HasFlag(FileAttributes.Encrypted)) parts.Add("encrypted");
        if (a.HasFlag(FileAttributes.ReparsePoint)) parts.Add("link");
        if (a.HasFlag(FileAttributes.Offline) || ((int)a & 0x00400000) != 0) parts.Add("cloud-only");
        return parts.Count == 0 ? "normal" : string.Join(", ", parts);
    }

    public static string Abbreviate(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var home = Platform.Home;
        if (string.Equals(path, home, StringComparison.OrdinalIgnoreCase)) return "~";
        if (path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return "~" + path[home.Length..];
        return path;
    }
}

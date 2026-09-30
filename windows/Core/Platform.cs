using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DiskWatch.Core;

/// <summary>
/// Everything OS-specific. Windows is the target; the macOS branches exist only so the app can be
/// developed and visually checked on a Mac.
/// </summary>
public static partial class Platform
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    public static bool IsAdmin
    {
        get
        {
            if (Environment.GetEnvironmentVariable("DISKWATCH_NO_ELEVATE") != null) return true;
            if (OperatingSystem.IsWindows())
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            return Environment.IsPrivilegedProcess;
        }
    }

    public static string UserName => Environment.UserName;
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string WindowsDir => IsWindows ? Environment.GetFolderPath(Environment.SpecialFolder.Windows) : "/Windows";
    public static string ProgramFiles => IsWindows ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) : "/Applications";
    public static string ProgramFilesX86 => IsWindows ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) : "/Applications (x86)";
    public static string ProgramData => IsWindows ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) : "/Library";
    public static string UsersDir => Path.GetDirectoryName(Home) ?? Home;
    public static string SystemDrive => IsWindows ? (Path.GetPathRoot(WindowsDir) ?? @"C:\") : "/";
    public static string Downloads => Path.Join(Home, "Downloads");
    public static string TempDir => Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
    public static string ExePath => Environment.ProcessPath ?? "";

    /// <summary>Folders that are never descended into (they mirror other data or are virtual).</summary>
    public static IEnumerable<string> AlwaysSkip(string root)
    {
        if (!IsWindows)
        {
            yield return "/System/Volumes/Data";
            yield return "/dev";
            yield return "/Volumes";
        }
    }

    // MARK: Volumes

    public sealed record Volume(string Path, string Name, long Total, long Available, bool IsFixed)
    {
        public long Used => Math.Max(0, Total - Available);
        public double Fraction => Total > 0 ? (double)Used / Total : 0;
    }

    public static List<Volume> Volumes()
    {
        var list = new List<Volume>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.TotalSize <= 0) continue;
                if (IsWindows)
                {
                    if (d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                }
                else if (d.Name != "/" && !d.Name.StartsWith("/Volumes/")) continue;
                string label = string.IsNullOrWhiteSpace(d.VolumeLabel) || !IsWindows ? (d.Name == "/" ? "Macintosh HD" : Path.GetFileName(d.Name.TrimEnd('/'))) : d.VolumeLabel;
                string name = IsWindows ? $"{label} ({d.Name.TrimEnd('\\')})" : label;
                list.Add(new Volume(d.Name, name, d.TotalSize, d.AvailableFreeSpace, d.DriveType == DriveType.Fixed));
            }
            catch { /* drive vanished or not accessible */ }
        }
        return list;
    }

    public static long ClusterSize(string path)
    {
        if (!IsWindows) return 4096;
        var root = Path.GetPathRoot(path);
        if (root != null && GetDiskFreeSpaceW(root, out uint spc, out uint bps, out _, out _)) return (long)spc * bps;
        return 4096;
    }

    public static bool IsVolumeRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        return root != null && string.Equals(root.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    // MARK: Shell actions (run as the signed-in user's shell)

    public static void Reveal(IEnumerable<string> paths)
    {
        var first = paths.FirstOrDefault();
        if (first == null) return;
        if (IsWindows) Start("explorer.exe", $"/select,\"{first}\"");
        else Start("/usr/bin/open", "-R " + string.Join(' ', paths.Select(Quote)));
    }

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    /// <summary>Windows: the Explorer Properties dialog. macOS dev build: Quick Look.</summary>
    public static void Properties(string path)
    {
        if (IsWindows) SHObjectProperties(IntPtr.Zero, 2 /* SHOP_FILEPATH */, path, null);
        else Start("/usr/bin/qlmanage", "-p " + Quote(path));
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    /// <summary>Relaunch elevated (UAC). Only needed if the manifest was bypassed.</summary>
    public static bool RelaunchAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch { return false; }
    }

    private static void Start(string exe, string args)
    {
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true }); } catch { }
    }

    public static (int Status, string Output) Run(string exe, string args)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            })!;
            string outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, outp);
        }
        catch (Exception e) { return (-1, e.Message); }
    }

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    // MARK: Recycle Bin

    public static (long Size, long Count) RecycleBinInfo()
    {
        if (!IsWindows)
        {
            var trash = Path.Join(Home, ".Trash");
            var n = Directory.Exists(trash) ? DiskScanner.Measure(trash) : null;
            return (n?.Allocated ?? 0, n?.FileCount ?? 0);
        }
        var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    public static bool EmptyRecycleBin()
    {
        if (!IsWindows) return false;
        // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND — DiskWatch already confirmed.
        int hr = SHEmptyRecycleBinW(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
        return hr == 0 || hr == unchecked((int)0x8000FFFF); // E_UNEXPECTED = already empty
    }

    /// <summary>Send to the Recycle Bin. If an item is too big for the bin, Windows asks before deleting it outright.</summary>
    public static void Recycle(string path)
    {
        if (!IsWindows)
        {
            var dest = Path.Join(Home, ".Trash", Path.GetFileName(path));
            int i = 2;
            while (File.Exists(dest) || Directory.Exists(dest)) dest = Path.Join(Home, ".Trash", $"{Path.GetFileNameWithoutExtension(path)} {i++}{Path.GetExtension(path)}");
            if (Directory.Exists(path)) Directory.Move(path, dest); else File.Move(path, dest);
            return;
        }
        var op = new SHFILEOPSTRUCTW
        {
            wFunc = 0x3, // FO_DELETE
            pFrom = path + "\0\0",
            // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING
            fFlags = 0x40 | 0x10 | 0x4 | 0x400 | 0x4000,
        };
        int rc = SHFileOperationW(ref op);
        if (op.fAnyOperationsAborted) throw new IOException("Cancelled.");
        if (rc != 0) throw new IOException(ShellError(rc));
    }

    private static string ShellError(int rc) => rc switch
    {
        0x2 or 0x7C => "The item no longer exists.",
        0x5 or 0x78 => "Access denied — the item is protected or in use.",
        0x20 => "The item is in use by another program.",
        _ => $"Windows couldn't move it to the Recycle Bin (code 0x{rc:X}).",
    };

    // MARK: Volume shadow copies (restore points) — Windows' counterpart to local snapshots

    public sealed record Shadow(string Id, string Created, string Volume);

    public static List<Shadow> ShadowCopies()
    {
        var list = new List<Shadow>();
        if (!IsWindows) return list;
        var (status, output) = Run("vssadmin.exe", "list shadows");
        if (status != 0) return list;
        string? id = null, created = "", vol = "";
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Contents of shadow copy set", StringComparison.OrdinalIgnoreCase))
                created = line.Contains("creation time:") ? line[(line.IndexOf("creation time:") + 14)..].Trim() : "";
            else if (line.StartsWith("Shadow Copy ID:", StringComparison.OrdinalIgnoreCase)) id = line[15..].Trim();
            else if (line.StartsWith("Original Volume:", StringComparison.OrdinalIgnoreCase))
            {
                vol = line[16..].Trim();
                if (id != null) { list.Add(new Shadow(id, created ?? "", vol)); id = null; }
            }
        }
        return list;
    }

    public static (bool Ok, string Message) DeleteShadow(string id)
    {
        var (status, output) = Run("vssadmin.exe", $"delete shadows /Shadow={id} /Quiet");
        return (status == 0, output.Trim());
    }

    // MARK: P/Invoke

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHObjectProperties(IntPtr hwnd, uint shopType, string name, string? page);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(IntPtr hwnd, string? root, uint flags);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHQueryRecycleBinW(string? root, ref SHQUERYRBINFO info);

    [StructLayout(LayoutKind.Sequential)] // default (8-byte) packing on x64/arm64
    private struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }
}

using System.Diagnostics;
using DiskWatch.Core;

namespace DiskWatch;

/// <summary><c>DiskWatch --selftest [path]</c> — exercises the scanner and safety rules without any UI.</summary>
public static class SelfTest
{
    public static void Run(string[] args)
    {
        int i = Array.IndexOf(args, "--selftest");
        string path = i + 1 < args.Length ? args[i + 1] : Platform.Home;
        Console.WriteLine($"user={Platform.UserName} admin={Platform.IsAdmin} home={Platform.Home} os={Environment.OSVersion}");
        var sw = Stopwatch.StartNew();
        var s = new DiskScanner();
        var root = s.Scan(path);
        if (root == null) { Console.WriteLine("scan failed"); return; }
        var p = s.Progress.Take();
        Console.WriteLine($"scanned {path} in {sw.Elapsed.TotalSeconds:0.00}s: {root.FileCount} files, {root.DirCount} dirs, {p.Errors} errors");
        Console.WriteLine($"allocated={root.Allocated} ({Fmt.Bytes(root.Allocated)}) logical={Fmt.Bytes(root.Logical)}");
        foreach (var c in root.Children.Take(8)) Console.WriteLine($"  {Fmt.Bytes(c.Allocated),-10} {c.Name}");
        var d = DerivedStats.Build(root, SizeMode.Allocated);
        Console.WriteLine("categories: " + string.Join(", ", d.Categories.Take(5).Select(c => $"{c.Category.Title()}={Fmt.Bytes(c.Allocated)}")));
        Console.WriteLine($"unreadable dirs: {d.Unreadable.Count}, excluded: {d.Excluded.Count}, links: {d.Links}");
        if (d.Files.FirstOrDefault() is { } f) Console.WriteLine($"find() ok: {ReferenceEquals(root.Find(f.FullPath), f)} {f.FullPath}");

        string h = Platform.Home, w = Platform.WindowsDir, l = Platform.LocalAppData, r = Platform.RoamingAppData;
        var samples = new[]
        {
            Platform.SystemDrive, w, Path.Join(w, "System32", "kernel32.dll"), Path.Join(w, "Temp", "x.tmp"),
            Path.Join(w, "SoftwareDistribution", "Download", "abc"), Path.Join(w, "WinSxS", "x"), Path.Join(w, "Installer", "x.msi"),
            Path.Join(Platform.SystemDrive, "pagefile.sys"), Path.Join(Platform.SystemDrive, "System Volume Information"),
            Path.Join(Platform.SystemDrive, "Windows.old"), Platform.ProgramFiles, Path.Join(Platform.ProgramFiles, "SomeApp"),
            Path.Join(Platform.ProgramFiles, "WindowsApps", "x"), Path.Join(Platform.ProgramData, "SomeVendor"),
            h, Path.Join(h, "NTUSER.DAT"), Path.Join(h, "Documents", "report.pdf"), Path.Join(h, "Downloads", "setup.exe"),
            Path.Join(h, "OneDrive", "x.docx"), Path.Join(h, ".ssh", "id_ed25519"), Path.Join(l, "Temp", "x"),
            Path.Join(l, "Google", "Chrome", "User Data", "Default", "Cache", "f_000"), Path.Join(r, "Microsoft", "Credentials", "x"),
            Path.Join(l, "SomeApp", "settings.json"), Path.Join(Platform.UsersDir, "someoneelse"), SafetyClassifier.RecycleBinPath,
        };
        Console.WriteLine("\nsafety:");
        foreach (var sp in samples)
        {
            var v = SafetyClassifier.Classify(sp, 0);
            Console.WriteLine($"  {v.Level.Short(),-9} {sp}  — {v.Reasons.FirstOrDefault()}");
        }
    }
}

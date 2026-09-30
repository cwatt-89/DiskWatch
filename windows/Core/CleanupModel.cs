namespace DiskWatch.Core;

public sealed class CleanupModel
{
    public enum SectionKind { System, User, Developer, Apps, Downloads, Personal }

    public static string SectionTitle(SectionKind s) => s switch
    {
        SectionKind.System => "System Junk",
        SectionKind.User => "User Caches & Logs",
        SectionKind.Developer => "Developer",
        SectionKind.Apps => "App Caches",
        SectionKind.Downloads => "Downloads & Installers",
        _ => "Large Personal Data (review carefully)",
    };

    public abstract record Target;
    public sealed record Contents(string Dir) : Target;
    public sealed record OlderThan(string Dir, int Days) : Target;
    public sealed record Matching(string Dir, string[] Exts) : Target;
    public sealed record Pattern(string Dir, string Glob) : Target;
    public sealed record SingleItem(string Path) : Target;
    /// <summary>Contents of <c>Rel</c> inside every subfolder of <c>Dir</c> (e.g. each Firefox profile's cache2).</summary>
    public sealed record EachSubfolder(string Dir, string Rel) : Target;
    public sealed record RecycleBin : Target;

    public sealed record Item(string Id, string Title, string Detail, SectionKind Section, SafetyLevel Safety, Target[] Targets,
                              bool PermanentOnly = false, bool DefaultChecked = true);

    public sealed class Measure
    {
        public long Size;
        public int Count;
        public List<(string Path, long Size)> Paths = new();
    }

    public readonly List<Item> Items = Catalog();
    public readonly Dictionary<string, Measure> Measures = new();
    public readonly HashSet<string> Measuring = new();
    public readonly HashSet<string> Checked = new();
    public bool HasScanned;
    public List<Platform.Shadow> Snapshots = new();
    public string? SnapshotMessage;

    /// <summary>Raised (on a worker thread) whenever sizes or snapshots change.</summary>
    public event Action? Changed;
    private readonly object _lock = new();

    public bool IsMeasuring { get { lock (_lock) return Measuring.Count > 0; } }

    public List<Item> VisibleItems
    {
        get
        {
            lock (_lock)
            {
                if (!HasScanned) return Items.ToList();
                return Items.Where(i => (Measures.TryGetValue(i.Id, out var m) && m.Count > 0) || Measuring.Contains(i.Id)).ToList();
            }
        }
    }

    public Measure? Get(string id) { lock (_lock) return Measures.GetValueOrDefault(id); }
    public bool IsMeasuringItem(string id) { lock (_lock) return Measuring.Contains(id); }

    public long SelectedSize { get { lock (_lock) return Items.Where(i => Checked.Contains(i.Id)).Sum(i => Measures.GetValueOrDefault(i.Id)?.Size ?? 0); } }
    public long TotalFound { get { lock (_lock) return Measures.Values.Sum(m => m.Size); } }

    public void ScanAll()
    {
        lock (_lock)
        {
            HasScanned = true;
            Measures.Clear();
            Checked.Clear();
            foreach (var i in Items) Measuring.Add(i.Id);
        }
        Changed?.Invoke();
        foreach (var item in Items)
        {
            Task.Run(() =>
            {
                var m = MeasureTargets(item.Targets);
                lock (_lock)
                {
                    Measures[item.Id] = m;
                    Measuring.Remove(item.Id);
                    if (item.DefaultChecked && item.Safety == SafetyLevel.Safe && m.Count > 0) Checked.Add(item.Id);
                }
                Changed?.Invoke();
            });
        }
        LoadSnapshots();
    }

    public void RefreshSizes()
    {
        if (!HasScanned) return;
        foreach (var item in Items)
        {
            Task.Run(() =>
            {
                var m = MeasureTargets(item.Targets);
                lock (_lock) Measures[item.Id] = m;
                Changed?.Invoke();
            });
        }
    }

    public void Toggle(string id, bool on)
    {
        lock (_lock) { if (on) Checked.Add(id); else Checked.Remove(id); }
        Changed?.Invoke();
    }

    public void SetChecked(IEnumerable<string> ids)
    {
        lock (_lock) { Checked.Clear(); foreach (var i in ids) Checked.Add(i); }
        Changed?.Invoke();
    }

    public List<(string Path, long Size)> TargetsFor(IEnumerable<string> ids)
    {
        lock (_lock)
        {
            var set = ids.ToHashSet();
            return Items.Where(i => set.Contains(i.Id)).SelectMany(i => Measures.GetValueOrDefault(i.Id)?.Paths ?? new()).ToList();
        }
    }

    public bool AnyPermanentOnly(IEnumerable<string> ids) { var s = ids.ToHashSet(); return Items.Any(i => s.Contains(i.Id) && i.PermanentOnly); }

    public void LoadSnapshots()
    {
        Task.Run(() =>
        {
            var s = Platform.ShadowCopies();
            lock (_lock) Snapshots = s;
            Changed?.Invoke();
        });
    }

    public void DeleteSnapshot(Platform.Shadow s)
    {
        Task.Run(() =>
        {
            var (ok, msg) = Platform.DeleteShadow(s.Id);
            FileOps.Log(ok ? "SHADOW" : "FAILED", s.Id, 0, msg);
            lock (_lock) SnapshotMessage = ok ? $"Deleted restore point from {s.Created}." : $"Couldn't delete it: {msg}";
            LoadSnapshots();
        });
    }

    // MARK: Measuring

    public static Measure MeasureTargets(IEnumerable<Target> targets)
    {
        var m = new Measure();
        void AddNode(string path, FileNode n)
        {
            m.Paths.Add((path, n.Allocated));
            m.Size += n.Allocated;
            m.Count += n.IsDirectory ? Math.Max(1, n.FileCount) : 1;
        }
        foreach (var t in targets)
        {
            switch (t)
            {
                case Contents c when Directory.Exists(c.Dir):
                    if (DiskScanner.Measure(c.Dir) is { } node)
                        foreach (var ch in node.Children) AddNode(Path.Join(c.Dir, ch.Name), ch);
                    break;
                case OlderThan o when Directory.Exists(o.Dir):
                    if (DiskScanner.Measure(o.Dir) is { } on)
                    {
                        var cutoff = DateTime.UtcNow.AddDays(-o.Days).Ticks;
                        foreach (var ch in on.Children.Where(ch => ch.ModifiedTicks < cutoff && !ch.Name.StartsWith('.') && !ch.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                            AddNode(Path.Join(o.Dir, ch.Name), ch);
                    }
                    break;
                case Matching x when Directory.Exists(x.Dir):
                    if (DiskScanner.Measure(x.Dir) is { } xn)
                        foreach (var ch in xn.Children.Where(ch => !ch.IsDirectory && x.Exts.Contains(ch.Extension)))
                            AddNode(Path.Join(x.Dir, ch.Name), ch);
                    break;
                case Pattern pt when Directory.Exists(pt.Dir):
                    foreach (var f in SafeFiles(pt.Dir, pt.Glob))
                        if (DiskScanner.Measure(f) is { } fn) AddNode(f, fn);
                    break;
                case SingleItem s when File.Exists(s.Path) || Directory.Exists(s.Path):
                    if (DiskScanner.Measure(s.Path) is { } sn && sn.Allocated > 0) AddNode(s.Path, sn);
                    break;
                case EachSubfolder e when Directory.Exists(e.Dir):
                    foreach (var sub in SafeDirs(e.Dir))
                    {
                        var inner = Path.Join(sub, e.Rel);
                        foreach (var p in MeasureTargets(new[] { new Contents(inner) }).Paths) m.Paths.Add(p);
                    }
                    m.Size = m.Paths.Sum(p => p.Size);
                    m.Count = Math.Max(m.Count, m.Paths.Count);
                    break;
                case RecycleBin:
                    var (size, count) = Platform.RecycleBinInfo();
                    if (count > 0)
                    {
                        m.Paths.Add((SafetyClassifier.RecycleBinPath, size));
                        m.Size += size;
                        m.Count += (int)count;
                    }
                    break;
            }
        }
        m.Paths.Sort((a, b) => b.Size.CompareTo(a.Size));
        return m;
    }

    private static IEnumerable<string> SafeFiles(string dir, string glob)
    {
        try { return Directory.GetFiles(dir, glob); } catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); } catch { return Array.Empty<string>(); }
    }

    // MARK: Catalog

    private static List<Item> Catalog()
    {
        string h = Platform.Home, local = Platform.LocalAppData, roam = Platform.RoamingAppData, win = Platform.WindowsDir, pd = Platform.ProgramData;
        Target[] Caches(string root) => new[] { "Cache", "Code Cache", "GPUCache", "CachedData", "DawnCache", "Service Worker\\CacheStorage" }
            .Select(s => (Target)new Contents(Path.Join(root, s))).ToArray();
        Target[] ChromiumProfile(string userData) => new[] { "Default", "Profile 1", "Profile 2", "Profile 3" }
            .SelectMany(p => new[] { "Cache", "Code Cache", "GPUCache" }.Select(s => (Target)new Contents(Path.Join(userData, p, s)))).ToArray();

        return new List<Item>
        {
            new("userTemp", "User temp files", "Temporary files apps left in your Temp folder. Files still in use are skipped.",
                SectionKind.User, SafetyLevel.Safe, new Target[] { new Contents(Platform.TempDir) }),
            new("recycle", "Recycle Bin", "Items already in the Recycle Bin. Emptying it is permanent.",
                SectionKind.User, SafetyLevel.Safe, new Target[] { new RecycleBin() }, PermanentOnly: true),
            new("crashDumps", "App crash dumps", "Crash dumps written when apps crash. Only used for troubleshooting.",
                SectionKind.User, SafetyLevel.Safe, new Target[] { new Contents(Path.Join(local, "CrashDumps")) }),
            new("inetCache", "Internet cache", "Legacy web cache used by Windows components and older apps.",
                SectionKind.User, SafetyLevel.Safe, new Target[] { new Contents(Path.Join(local, "Microsoft", "Windows", "INetCache")) }),
            new("shaderCache", "Graphics shader caches", "DirectX, NVIDIA and AMD shader caches. Games rebuild them on next launch.",
                SectionKind.User, SafetyLevel.Safe, new Target[]
                {
                    new Contents(Path.Join(local, "D3DSCache")), new Contents(Path.Join(local, "NVIDIA", "DXCache")),
                    new Contents(Path.Join(local, "NVIDIA", "GLCache")), new Contents(Path.Join(local, "AMD", "DxCache")),
                    new Contents(Path.Join(local, "AMD", "GLCache")),
                }),
            new("thumbcache", "Thumbnail cache", "Explorer's thumbnail database. Explorer rebuilds it (some files may be locked while Explorer runs).",
                SectionKind.User, SafetyLevel.Safe, new Target[] { new Pattern(Path.Join(local, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db") }, DefaultChecked: false),

            new("winTemp", "Windows temp files", @"C:\Windows\Temp — temporary files from installers and system services.",
                SectionKind.System, SafetyLevel.Caution, new Target[] { new Contents(Path.Join(win, "Temp")) }, DefaultChecked: false),
            new("wuDownload", "Windows Update downloads", "Update packages Windows already installed. Windows re-downloads anything it still needs.",
                SectionKind.System, SafetyLevel.Caution, new Target[] { new Contents(Path.Join(win, "SoftwareDistribution", "Download")) }, DefaultChecked: false),
            new("deliveryOpt", "Delivery Optimization cache", "Update pieces Windows keeps to share with other PCs.",
                SectionKind.System, SafetyLevel.Caution, new Target[] { new Contents(Path.Join(win, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache")) }, DefaultChecked: false),
            new("wer", "Error reports", "Windows Error Reporting archives and queued reports.",
                SectionKind.System, SafetyLevel.Caution, new Target[]
                {
                    new Contents(Path.Join(pd, "Microsoft", "Windows", "WER", "ReportArchive")),
                    new Contents(Path.Join(pd, "Microsoft", "Windows", "WER", "ReportQueue")),
                    new Contents(Path.Join(local, "Microsoft", "Windows", "WER", "ReportArchive")),
                }, DefaultChecked: false),
            new("sysDumps", "System memory dumps", "Blue-screen dumps (MEMORY.DMP and minidumps).",
                SectionKind.System, SafetyLevel.Caution, new Target[] { new Contents(Path.Join(win, "Minidump")), new SingleItem(Path.Join(win, "MEMORY.DMP")) }, DefaultChecked: false),
            new("winLogs", "Windows logs", @"C:\Windows\Logs — setup and servicing logs. Files in use are skipped.",
                SectionKind.System, SafetyLevel.Caution, new Target[] { new Contents(Path.Join(win, "Logs")) }, DefaultChecked: false),

            new("npm", "npm / Yarn / pnpm caches", "Package-manager download caches.",
                SectionKind.Developer, SafetyLevel.Safe, new Target[]
                {
                    new Contents(Path.Join(local, "npm-cache")), new Contents(Path.Join(roam, "npm-cache")),
                    new Contents(Path.Join(local, "Yarn", "Cache")), new Contents(Path.Join(local, "pnpm", "store")),
                }),
            new("pip", "pip cache", "Python package download cache.",
                SectionKind.Developer, SafetyLevel.Safe, new Target[] { new Contents(Path.Join(local, "pip", "Cache")) }),
            new("nugetHttp", "NuGet HTTP cache", "Downloaded package metadata and archives.",
                SectionKind.Developer, SafetyLevel.Safe, new Target[] { new Contents(Path.Join(local, "NuGet", "v3-cache")), new Contents(Path.Join(local, "NuGet", "plugins-cache")) }),
            new("nugetPkgs", "NuGet global packages", "Every package version you've restored. Re-downloaded on next build.",
                SectionKind.Developer, SafetyLevel.Caution, new Target[] { new Contents(Path.Join(h, ".nuget", "packages")) }, DefaultChecked: false),
            new("gradle", "Gradle / Maven caches", "JVM build caches (re-downloaded on next build).",
                SectionKind.Developer, SafetyLevel.Caution, new Target[] { new Contents(Path.Join(h, ".gradle", "caches")), new Contents(Path.Join(h, ".m2", "repository")) }, DefaultChecked: false),
            new("cargo", "Cargo / Go module caches", "Rust registry and Go module caches.",
                SectionKind.Developer, SafetyLevel.Caution, new Target[] { new Contents(Path.Join(h, ".cargo", "registry", "cache")), new Contents(Path.Join(local, "go-build")) }, DefaultChecked: false),
            new("vscode", "VS Code caches", "Editor caches and cached extension installers.",
                SectionKind.Developer, SafetyLevel.Safe, Caches(Path.Join(roam, "Code")).Append(new Contents(Path.Join(roam, "Code", "CachedExtensionVSIXs"))).ToArray()),

            new("browsers", "Browser caches", "Chrome, Edge, Brave and Firefox page caches (not history, passwords or cookies).",
                SectionKind.Apps, SafetyLevel.Safe,
                ChromiumProfile(Path.Join(local, "Google", "Chrome", "User Data"))
                    .Concat(ChromiumProfile(Path.Join(local, "Microsoft", "Edge", "User Data")))
                    .Concat(ChromiumProfile(Path.Join(local, "BraveSoftware", "Brave-Browser", "User Data")))
                    .Append(new EachSubfolder(Path.Join(local, "Mozilla", "Firefox", "Profiles"), "cache2")).ToArray()),
            new("chat", "Chat app caches", "Discord, Slack and Teams caches.",
                SectionKind.Apps, SafetyLevel.Safe,
                Caches(Path.Join(roam, "discord")).Concat(Caches(Path.Join(roam, "Slack"))).Concat(Caches(Path.Join(roam, "Microsoft", "Teams")))
                    .Append(new Contents(Path.Join(local, "Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache"))).ToArray()),
            new("spotify", "Spotify cache", "Streaming cache (downloaded playlists re-download).",
                SectionKind.Apps, SafetyLevel.Safe, new Target[] { new Contents(Path.Join(local, "Spotify", "Storage")), new Contents(Path.Join(local, "Spotify", "Data")) }),
            new("adobe", "Adobe media cache", "Premiere/After Effects media cache files.",
                SectionKind.Apps, SafetyLevel.Safe, new Target[] { new Contents(Path.Join(roam, "Adobe", "Common", "Media Cache Files")), new Contents(Path.Join(roam, "Adobe", "Common", "Media Cache")) }),

            new("installers", "Installers in Downloads", ".exe, .msi, .msix and .iso files you've probably already installed.",
                SectionKind.Downloads, SafetyLevel.Safe, new Target[] { new Matching(Platform.Downloads, new[] { "exe", "msi", "msix", "msixbundle", "appx", "iso", "img", "dmg" }) }),
            new("oldDownloads", "Downloads older than 6 months", "Everything in Downloads that hasn't changed in 180 days.",
                SectionKind.Downloads, SafetyLevel.Caution, new Target[] { new OlderThan(Platform.Downloads, 180) }, DefaultChecked: false),

            new("windowsOld", "Previous Windows installation", "Windows.old lets you roll back a Windows upgrade. Remove it only if you're happy with the current version.",
                SectionKind.Personal, SafetyLevel.Danger, new Target[] { new SingleItem(Path.Join(Platform.SystemDrive, "Windows.old")) }, DefaultChecked: false),
            new("iosBackups", "iPhone / iPad backups", "Local device backups. These may be your only copy — make sure you have iCloud or another backup first.",
                SectionKind.Personal, SafetyLevel.Danger, new Target[]
                {
                    new Contents(Path.Join(roam, "Apple Computer", "MobileSync", "Backup")),
                    new Contents(Path.Join(h, "Apple", "MobileSync", "Backup")),
                }, DefaultChecked: false),
            new("docker", "Docker disk image", "Docker's WSL disk. Prefer `docker system prune` — deleting this removes all containers and images.",
                SectionKind.Personal, SafetyLevel.Danger, new Target[] { new Contents(Path.Join(local, "Docker", "wsl")) }, DefaultChecked: false),
        };
    }
}

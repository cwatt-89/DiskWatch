using System.Text;
using System.Text.Json;

namespace DiskWatch.Core;

public enum Tab { Overview, Tree, Treemap, Files, Types, Cleanup, Duplicates }

[Flags]
public enum Topic
{
    None = 0, Scan = 1, Tree = 2, Selection = 4, Tab = 8, Settings = 16, Progress = 32, Deletion = 64,
    Cleanup = 128, Duplicates = 256, Filter = 512, Derived = 1024, Volumes = 2048, Error = 4096,
}

public sealed record FilesFilter(string Text = "", FileCategory? Category = null, string? Ext = null, long MinSize = 0, int OlderThanDays = 0, int Limit = 2000);

public sealed record DeletionItem(string Path, long Size, bool IsDirectory, SafetyVerdict Verdict);

public sealed class DeletionRequest
{
    public List<DeletionItem> Items = new();
    public bool Permanent;
    public string Source = "Selection";
    public bool LockMode;
    public List<DeletionItem> Allowed => Items.Where(i => i.Verdict.Level != SafetyLevel.Protected).ToList();
    public List<DeletionItem> Blocked => Items.Where(i => i.Verdict.Level == SafetyLevel.Protected).ToList();
    public bool NeedsTypedConfirmation => Allowed.Any(i => i.Verdict.Level == SafetyLevel.Danger);
    public long TotalAllowed => Allowed.Sum(i => i.Size);
}

public sealed class DeletionResult
{
    public List<(string Path, long Size)> Removed = new();
    public List<(string Path, string Reason)> Failed = new();
    public long Freed => Removed.Sum(r => r.Size);
}

public sealed class Settings
{
    public SizeMode SizeMode { get; set; } = SizeMode.Allocated;
    public bool StayOnVolume { get; set; } = true;
    public List<string> ExcludedPaths { get; set; } = new();
    public List<string> RecentPaths { get; set; } = new();
    public bool DefaultPermanent { get; set; }

    private static string FilePath => Path.Join(Platform.LocalAppData, "DiskWatch", "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); } catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public sealed class AppState
{
    /// <summary>Marshals a callback onto the UI thread (set by the UI layer).</summary>
    public static Action<Action> Ui = a => a();

    public event Action<Topic>? Changed;
    public void Notify(Topic t) => Ui(() => Changed?.Invoke(t));

    public readonly Settings Settings = Settings.Load();
    public readonly CleanupModel Cleanup = new();
    public readonly DuplicatesModel Duplicates = new();

    public bool NeedsElevation;
    public FileNode? Root;
    public string? ScanPath;
    public bool IsScanning;
    public bool ScanWasCancelled;
    public ScanProgress.Snapshot Progress;
    public DateTime ScanStarted = DateTime.Now;
    public double ScanDuration;
    public DerivedStats Derived = new();
    public bool IsRebuilding;
    public Tab Tab = Tab.Tree;
    public List<FileNode> Selection = new();
    public bool ShowInspector = true;
    public int TreeVersion;
    public int SortStamp;
    public FileNode? TreemapRoot;
    public FilesFilter Filter = new();
    public List<Platform.Volume> Volumes = Platform.Volumes();
    public DeletionRequest? Deletion;
    public string? ErrorMessage;
    public Action<FileNode>? RevealHandler;
    private DiskScanner? _scanner;

    public AppState()
    {
        Cleanup.Changed += () => Notify(Topic.Cleanup);
        Duplicates.Changed += () => Notify(Topic.Duplicates);
    }

    public SizeMode SizeMode => Settings.SizeMode;
    public FileNode? Focused => Selection.FirstOrDefault() ?? Root;

    public Platform.Volume? RootVolume
    {
        get
        {
            if (ScanPath == null) return null;
            return Volumes.Where(v => ScanPath.StartsWith(v.Path, StringComparison.OrdinalIgnoreCase))
                          .OrderByDescending(v => v.Path.Length).FirstOrDefault();
        }
    }

    // MARK: Settings

    public void SetSizeMode(SizeMode m)
    {
        Settings.SizeMode = m;
        Settings.Save();
        SortStamp++;
        Notify(Topic.Settings | Topic.Tree);
        RebuildDerived();
    }

    public void SetTab(Tab t) { Tab = t; Notify(Topic.Tab); }
    public void SetFilter(FilesFilter f) { Filter = f; Notify(Topic.Filter); }
    public void ToggleInspector() { ShowInspector = !ShowInspector; Notify(Topic.Settings); }
    public void SaveSettings() { Settings.Save(); Notify(Topic.Settings); }

    // MARK: Scanning

    public void Scan(string path)
    {
        CancelScan();
        var s = new DiskScanner(new DiskScanner.Options(Settings.StayOnVolume, Settings.ExcludedPaths.ToList()));
        _scanner = s;
        IsScanning = true;
        ScanWasCancelled = false;
        ScanPath = path;
        ScanStarted = DateTime.Now;
        Progress = default;
        Selection = new();
        TreemapRoot = null;
        Duplicates.Reset();
        Settings.RecentPaths = new[] { path }.Concat(Settings.RecentPaths.Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase))).Take(8).ToList();
        Settings.Save();
        var mode = SizeMode;
        Notify(Topic.Scan | Topic.Selection);

        Task.Run(async () =>
        {
            while (IsScanning && ReferenceEquals(_scanner, s))
            {
                Progress = s.Progress.Take();
                Notify(Topic.Progress);
                await Task.Delay(120);
            }
        });
        Task.Run(() =>
        {
            FileNode? root = null;
            try { root = s.Scan(path); } catch (Exception e) { ErrorMessage = e.Message; }
            var derived = root != null ? DerivedStats.Build(root, mode) : new DerivedStats();
            Ui(() =>
            {
                if (!ReferenceEquals(_scanner, s)) return;
                Progress = s.Progress.Take();
                ScanDuration = (DateTime.Now - ScanStarted).TotalSeconds;
                ScanWasCancelled = s.IsCancelled;
                Root = root;
                Derived = derived;
                IsScanning = false;
                _scanner = null;
                SortStamp++;
                TreeVersion++;
                Volumes = Platform.Volumes();
                if (root == null) ErrorMessage ??= $"Could not read {path}.";
                Changed?.Invoke(Topic.Scan | Topic.Tree | Topic.Derived | Topic.Volumes | Topic.Progress | Topic.Selection | (ErrorMessage != null ? Topic.Error : 0));
            });
        });
    }

    public void CancelScan() => _scanner?.Cancel();
    public void Rescan() { if (ScanPath != null) Scan(ScanPath); }

    public void Rescan(FileNode node)
    {
        if (!node.IsDirectory) return;
        if (node.Parent is not { } parent) { Rescan(); return; }
        var path = node.FullPath;
        var s = new DiskScanner(new DiskScanner.Options(Settings.StayOnVolume, Settings.ExcludedPaths.ToList()));
        Task.Run(() =>
        {
            var fresh = s.Scan(path);
            if (fresh == null) return;
            Ui(() =>
            {
                parent.ReplaceChild(node, fresh);
                Selection = new() { fresh };
                AfterTreeChange();
                Changed?.Invoke(Topic.Selection);
            });
        });
    }

    public void AfterTreeChange()
    {
        SortStamp++;
        TreeVersion++;
        Volumes = Platform.Volumes();
        Notify(Topic.Tree | Topic.Volumes);
        RebuildDerived();
    }

    public void RebuildDerived()
    {
        if (Root is not { } root) return;
        var mode = SizeMode;
        IsRebuilding = true;
        Task.Run(() =>
        {
            var d = DerivedStats.Build(root, mode);
            Ui(() =>
            {
                if (!ReferenceEquals(Root, root)) return;
                Derived = d;
                IsRebuilding = false;
                Changed?.Invoke(Topic.Derived);
            });
        });
    }

    // MARK: Selection & navigation

    public void SetSelection(List<FileNode> nodes) { Selection = nodes; Notify(Topic.Selection); }

    public void Select(FileNode n, bool reveal = true)
    {
        Selection = new() { n };
        Notify(Topic.Selection);
        if (reveal) RevealHandler?.Invoke(n);
    }

    public void ShowInTree(FileNode n)
    {
        Selection = new() { n };
        Tab = Tab.Tree;
        Notify(Topic.Tab | Topic.Selection);
        Ui(() => RevealHandler?.Invoke(n));
    }

    public void ShowInTreemap(FileNode n)
    {
        TreemapRoot = n.IsDirectory ? n : n.Parent;
        Selection = new() { n };
        Tab = Tab.Treemap;
        Notify(Topic.Tab | Topic.Selection);
    }

    public void ExcludeFromScans(string path)
    {
        if (!Settings.ExcludedPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) Settings.ExcludedPaths.Add(path);
        SaveSettings();
    }

    // MARK: Deletion

    public void RequestDelete(IEnumerable<FileNode> nodes, bool? permanent = null, string source = "Selection")
    {
        var list = nodes.ToList();
        var set = list.ToHashSet();
        var top = list.Where(n => !n.Ancestors.Any(set.Contains)).ToList();
        var items = top.Select(n => new DeletionItem(n.FullPath, n.Size(SizeMode), n.IsDirectory, SafetyClassifier.Classify(n))).ToList();
        if (items.Count == 0) return;
        Deletion = new DeletionRequest { Items = items, Permanent = permanent ?? Settings.DefaultPermanent, Source = source };
        Notify(Topic.Deletion);
    }

    public void RequestDelete(IEnumerable<(string Path, long Size)> paths, bool? permanent, bool lockMode, string source)
    {
        var kept = new List<(string Path, long Size)>();
        foreach (var p in paths.OrderBy(p => p.Path.Length))
            if (!kept.Any(k => p.Path.Equals(k.Path, StringComparison.OrdinalIgnoreCase) || p.Path.StartsWith(k.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                kept.Add(p);
        var items = kept.Select(p => new DeletionItem(p.Path, p.Size, Directory.Exists(p.Path), SafetyClassifier.Classify(p.Path))).ToList();
        if (items.Count == 0) return;
        Deletion = new DeletionRequest { Items = items, Permanent = permanent ?? Settings.DefaultPermanent, Source = source, LockMode = lockMode };
        Notify(Topic.Deletion);
    }

    public void CloseDeletion() { Deletion = null; Notify(Topic.Deletion); }

    public async Task<DeletionResult> Perform(DeletionRequest request)
    {
        var allowed = request.Allowed;
        bool permanent = request.Permanent;
        var result = await Task.Run(() =>
        {
            var r = new DeletionResult();
            foreach (var item in allowed)
            {
                // Re-check at the last moment in case something changed since the dialog opened.
                var verdict = SafetyClassifier.Classify(item.Path);
                if (verdict.Level == SafetyLevel.Protected)
                {
                    r.Failed.Add((item.Path, "Blocked: " + verdict.Reasons.FirstOrDefault()));
                    continue;
                }
                try
                {
                    if (permanent || item.Path == SafetyClassifier.RecycleBinPath)
                    {
                        FileOps.DeletePermanently(item.Path);
                        FileOps.Log("DELETE", item.Path, item.Size);
                    }
                    else
                    {
                        FileOps.MoveToRecycleBin(item.Path);
                        FileOps.Log("RECYCLE", item.Path, item.Size);
                    }
                    r.Removed.Add((item.Path, item.Size));
                }
                catch (Exception e)
                {
                    var why = FileOps.Describe(e);
                    r.Failed.Add((item.Path, why));
                    FileOps.Log("FAILED", item.Path, item.Size, why);
                }
            }
            return r;
        });

        bool touched = false;
        if (Root is { } root)
        {
            foreach (var (path, _) in result.Removed)
            {
                if (root.Find(path) is { } node && !ReferenceEquals(node, root))
                {
                    Selection.RemoveAll(s => ReferenceEquals(s, node) || s.IsDescendantOf(node));
                    if (TreemapRoot is { } tm && (ReferenceEquals(tm, node) || tm.IsDescendantOf(node))) TreemapRoot = node.Parent;
                    node.Detach();
                    touched = true;
                }
            }
            foreach (var (path, _) in result.Failed)
                if (root.Find(path) is { IsDirectory: true } node && Directory.Exists(path)) Rescan(node);
        }
        Duplicates.RemoveDeleted(result.Removed.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase));
        if (touched) AfterTreeChange(); else { Volumes = Platform.Volumes(); Notify(Topic.Volumes); }
        Cleanup.RefreshSizes();
        Notify(Topic.Selection | Topic.Duplicates);
        return result;
    }

    // MARK: Export

    public void ExportCsv(string file)
    {
        if (Root is not { } root) return;
        static string Esc(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("Path,Type,Size on Disk (bytes),File Size (bytes),Files,Folders,Modified,Attributes\n");
        var stack = new Stack<FileNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.IsDirectory || n.Allocated >= 10_000_000)
                sb.Append(string.Join(",", Esc(n.FullPath), n.IsDirectory ? "Folder" : "File", n.Allocated, n.Logical,
                    n.IsDirectory ? n.FileCount : 1, n.DirCount, n.Modified.ToString("O"), Esc(Fmt.Attributes(n.Attributes)))).Append('\n');
            if (n.IsDirectory) for (int i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
        try { File.WriteAllText(file, sb.ToString()); }
        catch (Exception e) { ErrorMessage = e.Message; Notify(Topic.Error); }
    }
}

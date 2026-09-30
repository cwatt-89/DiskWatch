using System.IO.Enumeration;

namespace DiskWatch.Core;

public sealed class ScanProgress
{
    private readonly object _lock = new();
    private int _files, _dirs, _errors;
    private long _bytes;
    private string _path = "";

    public void Add(int files, int dirs, long bytes, int errors, string path)
    {
        lock (_lock) { _files += files; _dirs += dirs; _bytes += bytes; _errors += errors; _path = path; }
    }

    public readonly record struct Snapshot(int Files, int Dirs, int Errors, long Bytes, string? Path);

    public Snapshot Take() { lock (_lock) return new(_files, _dirs, _errors, _bytes, _path); }
}

/// <summary>Multi-threaded directory walker built on FileSystemEnumerable (no per-file allocations beyond the node).</summary>
public sealed class DiskScanner
{
    public sealed record Options(bool StayOnVolume = true, IReadOnlyList<string>? ExcludedPaths = null);

    public readonly ScanProgress Progress = new();
    private readonly Options _options;
    private readonly object _cond = new();
    private readonly Stack<(FileNode Node, string Path)> _stack = new();
    private int _pending;
    private volatile bool _cancelled;
    private HashSet<string> _skip = new(StringComparer.OrdinalIgnoreCase);
    private long _cluster = 4096;

    public DiskScanner(Options? options = null) { _options = options ?? new Options(); }

    public bool IsCancelled => _cancelled;

    public void Cancel()
    {
        _cancelled = true;
        lock (_cond) Monitor.PulseAll(_cond);
    }

    private readonly record struct Entry(string Name, bool IsDir, FileAttributes Attr, long Length, long Mod, long Acc, long Cre);

    private static readonly EnumerationOptions EnumOptions = new()
    {
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
        MatchType = MatchType.Win32,
    };

    /// <summary>Scans rootPath and returns the aggregated tree. Blocks the calling thread.</summary>
    public FileNode? Scan(string rootPath)
    {
        rootPath = Path.GetFullPath(rootPath);
        FileSystemInfo info = Directory.Exists(rootPath) ? new DirectoryInfo(rootPath) : new FileInfo(rootPath);
        if (!info.Exists) return null;
        _cluster = Platform.ClusterSize(rootPath);
        _skip = new HashSet<string>(_options.ExcludedPaths ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var s in Platform.AlwaysSkip(rootPath)) _skip.Add(s);
        _skip.Remove(rootPath);

        bool isDir = info is DirectoryInfo;
        long len = info is FileInfo fi ? fi.Length : 0;
        var root = new FileNode(rootPath, null, isDir, false, len, Round(len, info.Attributes),
                                info.LastWriteTimeUtc.Ticks, info.LastAccessTimeUtc.Ticks, info.CreationTimeUtc.Ticks, info.Attributes);
        if (!isDir) return root;

        _stack.Push((root, rootPath));
        _pending = 1;
        int workers = int.TryParse(Environment.GetEnvironmentVariable("DW_WORKERS"), out var w) ? w : Math.Clamp(Environment.ProcessorCount, 2, 8);
        var threads = Enumerable.Range(0, workers).Select(_ => new Thread(Worker, 1 << 20) { IsBackground = true }).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        Aggregate(root);
        return root;
    }

    private long Round(long length, FileAttributes attr)
    {
        // Cloud placeholders (OneDrive etc.) report a length but take no space on disk.
        const FileAttributes recall = (FileAttributes)0x00400000 | (FileAttributes)0x00040000;
        if ((attr & (FileAttributes.Offline | recall)) != 0) return 0;
        if (length <= 0) return 0;
        return (length + _cluster - 1) / _cluster * _cluster;
    }

    private void Worker()
    {
        while (true)
        {
            (FileNode Node, string Path) item;
            lock (_cond)
            {
                while (_stack.Count == 0 && _pending > 0 && !_cancelled) Monitor.Wait(_cond);
                if (_stack.Count == 0 || _cancelled) { Monitor.PulseAll(_cond); return; }
                item = _stack.Pop();
            }
            var subdirs = Process(item.Node, item.Path);
            lock (_cond)
            {
                foreach (var s in subdirs) _stack.Push(s);
                _pending += subdirs.Count - 1;
                if (_pending == 0 || subdirs.Count > 0) Monitor.PulseAll(_cond);
            }
        }
    }

    private List<(FileNode, string)> Process(FileNode node, string path)
    {
        var subdirs = new List<(FileNode, string)>();
        var kids = new List<FileNode>();
        int files = 0, dirs = 0, errors = 0;
        long bytes = 0;
        try
        {
            var e = new FileSystemEnumerable<Entry>(path,
                (ref FileSystemEntry fe) => new Entry(fe.FileName.ToString(), fe.IsDirectory, fe.Attributes, fe.Length,
                    fe.LastWriteTimeUtc.UtcTicks, fe.LastAccessTimeUtc.UtcTicks, fe.CreationTimeUtc.UtcTicks), EnumOptions);
            foreach (var en in e)
            {
                bool link = (en.Attr & FileAttributes.ReparsePoint) != 0;
                // Junctions/symlinked folders are shown but never followed (avoids loops like "Application Data").
                bool dirLike = en.IsDir;
                long alloc = dirLike || (link && en.Length == 0) ? 0 : Round(en.Length, en.Attr);
                var child = new FileNode(en.Name, node, dirLike, link, en.Length, alloc, en.Mod, en.Acc, en.Cre, en.Attr);
                if (dirLike)
                {
                    var childPath = Path.Join(path, en.Name);
                    if (link || _skip.Contains(childPath)) child.Excluded = !link;
                    else subdirs.Add((child, childPath));
                    dirs++;
                }
                else
                {
                    files++;
                    bytes += child.Allocated;
                }
                kids.Add(child);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            node.Unreadable = true;
            errors++;
        }
        node.Children = kids;
        Progress.Add(files, dirs, bytes, errors, path);
        return subdirs;
    }

    /// <summary>Roll child totals up into every directory, sort children by size, and mark Program Files contents.</summary>
    public static void Aggregate(FileNode root)
    {
        var order = new List<FileNode> { root };
        for (int i = 0; i < order.Count; i++)
        {
            var n = order[i];
            if (!n.IsDirectory) continue;
            bool inApp = n.InProgramFiles || IsProgramDir(n.Name);
            foreach (var c in n.Children)
            {
                c.InProgramFiles = inApp;
                order.Add(c);
            }
        }
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var n = order[i];
            if (!n.IsDirectory) continue;
            long l = 0, a = 0;
            int f = 0, d = 0;
            foreach (var c in n.Children)
            {
                l += c.Logical;
                a += c.Allocated;
                if (c.IsDirectory) { d += 1 + c.DirCount; f += c.FileCount; } else f++;
            }
            n.Logical = l;
            n.Allocated = a;
            n.FileCount = f;
            n.DirCount = d;
            n.Children.Sort((x, y) => y.Allocated.CompareTo(x.Allocated));
        }
    }

    private static bool IsProgramDir(string name) =>
        name.Equals("Program Files", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase)
        || name.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".app", StringComparison.OrdinalIgnoreCase);

    /// <summary>Quick total for a single path (used by cleanup suggestions).</summary>
    public static FileNode? Measure(string path) => new DiskScanner().Scan(path);
}

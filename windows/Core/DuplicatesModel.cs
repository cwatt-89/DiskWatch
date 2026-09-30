using System.Security.Cryptography;

namespace DiskWatch.Core;

public sealed class DuplicatesModel
{
    public sealed class Group
    {
        public long Size;
        public List<FileNode> Files = new();
        public long Wasted => Size * Math.Max(0, Files.Count - 1);
    }

    public List<Group> Groups = new();
    public bool IsRunning;
    public double Progress;
    public string Status = "";
    public readonly HashSet<FileNode> Marked = new();
    public long MinSize = 1_000_000;
    public bool HasRun;
    private CancellationTokenSource? _cts;

    /// <summary>Raised on a worker thread while running, and on the caller's thread for mark changes.</summary>
    public event Action? Changed;

    public long TotalWasted => Groups.Sum(g => g.Wasted);
    public long MarkedSize => Marked.Sum(f => f.Allocated);
    public List<FileNode> MarkedNodes => Groups.SelectMany(g => g.Files).Where(Marked.Contains).ToList();

    public void Reset()
    {
        _cts?.Cancel();
        Groups = new();
        Marked.Clear();
        HasRun = false;
        IsRunning = false;
        Status = "";
    }

    public void Run(List<FileNode> files)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsRunning = true;
        HasRun = true;
        Progress = 0;
        Groups = new();
        Marked.Clear();
        Status = "Grouping by size…";
        Changed?.Invoke();
        long min = MinSize;
        var candidates = files.Where(f => f.Logical >= min && !f.IsLink)
            .GroupBy(f => f.Logical).Where(g => g.Count() > 1).SelectMany(g => g).Select(f => (f, f.FullPath)).ToList();

        Task.Run(() =>
        {
            var found = Find(candidates, cts.Token, (p, s) => { Progress = p; Status = s; Changed?.Invoke(); });
            if (cts.IsCancellationRequested) return;
            Groups = found.OrderByDescending(g => g.Wasted).ToList();
            IsRunning = false;
            Status = found.Count == 0 ? "No duplicates found." : $"{found.Count} groups of identical files.";
            Changed?.Invoke();
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        IsRunning = false;
        Status = "Stopped.";
        Changed?.Invoke();
    }

    public enum Keep { Newest, Oldest, ShortestPath }

    public void AutoMark(Keep keep)
    {
        Marked.Clear();
        foreach (var g in Groups)
        {
            var keeper = keep switch
            {
                Keep.Newest => g.Files.MaxBy(f => f.ModifiedTicks),
                Keep.Oldest => g.Files.MinBy(f => f.ModifiedTicks),
                _ => g.Files.MinBy(f => f.FullPath.Length),
            };
            foreach (var f in g.Files) if (!ReferenceEquals(f, keeper)) Marked.Add(f);
        }
        Changed?.Invoke();
    }

    /// <summary>Marks/unmarks a copy, but never lets every copy in a group be marked.</summary>
    public bool Toggle(FileNode f)
    {
        if (Marked.Remove(f)) { Changed?.Invoke(); return true; }
        var g = Groups.FirstOrDefault(g => g.Files.Contains(f));
        if (g != null && g.Files.All(x => ReferenceEquals(x, f) || Marked.Contains(x)))
        {
            Status = $"Keep at least one copy of {f.Name} — DiskWatch won't mark every copy.";
            Changed?.Invoke();
            return false;
        }
        Marked.Add(f);
        Changed?.Invoke();
        return true;
    }

    public void RemoveDeleted(HashSet<string> paths)
    {
        if (Groups.Count == 0) return;
        foreach (var g in Groups) g.Files.RemoveAll(f => f.Parent == null || paths.Contains(f.FullPath));
        Groups.RemoveAll(g => g.Files.Count < 2);
        var alive = Groups.SelectMany(g => g.Files).ToHashSet();
        Marked.IntersectWith(alive);
    }

    private static List<Group> Find(List<(FileNode Node, string Path)> candidates, CancellationToken ct, Action<double, string> progress)
    {
        var result = new List<Group>();
        double total = Math.Max(1, candidates.Sum(c => (double)c.Node.Logical)), done = 0;
        var last = DateTime.MinValue;
        foreach (var sizeGroup in candidates.GroupBy(c => c.Node.Logical))
        {
            if (ct.IsCancellationRequested) return new();
            var partial = sizeGroup.Select(c => (c, h: Hash(c.Path, false))).Where(x => x.h != null).GroupBy(x => x.h!, StringComparer.Ordinal);
            foreach (var same in partial.Where(g => g.Count() > 1))
            {
                var full = new Dictionary<string, List<FileNode>>();
                foreach (var (c, _) in same)
                {
                    if (ct.IsCancellationRequested) return new();
                    var h = Hash(c.Path, true);
                    if (h != null) { if (!full.TryGetValue(h, out var l)) full[h] = l = new(); l.Add(c.Node); }
                    done += c.Node.Logical;
                    if ((DateTime.UtcNow - last).TotalMilliseconds > 150)
                    {
                        last = DateTime.UtcNow;
                        progress(Math.Min(1, done / total), "Comparing " + Path.GetFileName(c.Path));
                    }
                }
                foreach (var files in full.Values.Where(l => l.Count > 1))
                    result.Add(new Group { Size = files[0].Allocated, Files = files });
            }
        }
        progress(1, "Done");
        return result;
    }

    private static string? Hash(string path, bool full)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            using var sha = SHA256.Create();
            if (full) return Convert.ToHexString(sha.ComputeHash(fs));
            var buf = new byte[64 * 1024];
            int n = fs.Read(buf, 0, buf.Length);
            sha.TransformBlock(buf, 0, n, null, 0);
            if (fs.Length > 128 * 1024)
            {
                fs.Seek(-64 * 1024, SeekOrigin.End);
                n = fs.Read(buf, 0, buf.Length);
            }
            else n = 0;
            sha.TransformFinalBlock(buf, 0, n);
            return Convert.ToHexString(sha.Hash!);
        }
        catch { return null; }
    }
}

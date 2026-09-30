namespace DiskWatch.Core;

public enum SizeMode { Allocated, Logical }

public static class SizeModeExt
{
    public static string Title(this SizeMode m) => m == SizeMode.Allocated ? "Size on Disk" : "File Size";
    public static SizeMode Other(this SizeMode m) => m == SizeMode.Allocated ? SizeMode.Logical : SizeMode.Allocated;
}

/// <summary>One file or folder in a scan. Directories aggregate the sizes of everything below them.</summary>
public sealed class FileNode
{
    public string Name;
    public FileNode? Parent;
    public readonly bool IsDirectory;
    public readonly bool IsLink;
    public long Logical;
    public long Allocated;
    public int FileCount;
    public int DirCount;
    public readonly long ModifiedTicks;
    public readonly long AccessedTicks;
    public readonly long CreatedTicks;
    public readonly FileAttributes Attributes;
    public List<FileNode> Children = Empty;

    public bool Unreadable;
    public bool Excluded;
    public bool InProgramFiles;
    public int SortStamp = -1;

    public static readonly List<FileNode> Empty = new(0);

    public FileNode(string name, FileNode? parent, bool isDirectory, bool isLink, long logical, long allocated,
                    long modified, long accessed, long created, FileAttributes attributes)
    {
        Name = name;
        Parent = parent;
        IsDirectory = isDirectory;
        IsLink = isLink;
        Logical = isDirectory ? 0 : logical;
        Allocated = isDirectory ? 0 : allocated;
        ModifiedTicks = modified;
        AccessedTicks = accessed;
        CreatedTicks = created;
        Attributes = attributes;
    }

    public long Size(SizeMode m) => m == SizeMode.Allocated ? Allocated : Logical;

    public DateTime Modified => new(ModifiedTicks, DateTimeKind.Utc);
    public DateTime Accessed => new(AccessedTicks, DateTimeKind.Utc);
    public DateTime Created => new(CreatedTicks, DateTimeKind.Utc);

    public string FullPath
    {
        get
        {
            var parts = new List<string>();
            for (var n = this; n != null; n = n.Parent) parts.Add(n.Name);
            var result = parts[^1];
            for (int i = parts.Count - 2; i >= 0; i--)
            {
                if (!result.EndsWith(System.IO.Path.DirectorySeparatorChar)) result += System.IO.Path.DirectorySeparatorChar;
                result += parts[i];
            }
            return result;
        }
    }

    public string DisplayName => Parent == null ? FullPath : Name;

    public string Extension
    {
        get
        {
            if (IsDirectory) return "";
            int dot = Name.LastIndexOf('.');
            return dot <= 0 || dot == Name.Length - 1 ? "" : Name[(dot + 1)..].ToLowerInvariant();
        }
    }

    public int ItemCount => FileCount + DirCount;

    public IEnumerable<FileNode> Ancestors
    {
        get { for (var n = Parent; n != null; n = n.Parent) yield return n; }
    }

    public bool IsDescendantOf(FileNode other)
    {
        for (var n = Parent; n != null; n = n.Parent) if (ReferenceEquals(n, other)) return true;
        return false;
    }

    public int Depth => Ancestors.Count();

    public double FractionOfParent(SizeMode m)
    {
        if (Parent == null) return 1;
        long total = Parent.Size(m);
        return total > 0 ? (double)Size(m) / total : 0;
    }

    public FileNode? Child(string name) => Children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Remove this node from its parent and subtract its totals from every ancestor.</summary>
    public void Detach()
    {
        var p = Parent;
        if (p == null) return;
        p.Children.Remove(this);
        int files = IsDirectory ? FileCount : 1;
        int dirs = IsDirectory ? DirCount + 1 : 0;
        for (var a = p; a != null; a = a.Parent)
        {
            a.Logical -= Logical;
            a.Allocated -= Allocated;
            a.FileCount -= files;
            a.DirCount -= dirs;
        }
        Parent = null;
    }

    /// <summary>Replace a child with a freshly scanned node, fixing totals up the chain.</summary>
    public void ReplaceChild(FileNode old, FileNode fresh)
    {
        int idx = Children.IndexOf(old);
        if (idx < 0) return;
        fresh.Name = old.Name;
        fresh.Parent = this;
        fresh.InProgramFiles = old.InProgramFiles;
        Children[idx] = fresh;
        long dl = fresh.Logical - old.Logical, da = fresh.Allocated - old.Allocated;
        int df = (fresh.IsDirectory ? fresh.FileCount : 1) - (old.IsDirectory ? old.FileCount : 1);
        int dd = (fresh.IsDirectory ? fresh.DirCount + 1 : 0) - (old.IsDirectory ? old.DirCount + 1 : 0);
        for (var a = this; a != null; a = a.Parent)
        {
            a.Logical += dl;
            a.Allocated += da;
            a.FileCount += df;
            a.DirCount += dd;
        }
        old.Parent = null;
    }

    /// <summary>Find a node by absolute path under this root.</summary>
    public FileNode? Find(string target)
    {
        var rootPath = FullPath;
        if (string.Equals(target, rootPath, StringComparison.OrdinalIgnoreCase)) return this;
        var sep = System.IO.Path.DirectorySeparatorChar;
        var prefix = rootPath.EndsWith(sep) ? rootPath : rootPath + sep;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var node = this;
        foreach (var comp in target[prefix.Length..].Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = node.Child(comp);
            if (next == null) return null;
            node = next;
        }
        return node;
    }
}

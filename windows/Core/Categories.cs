namespace DiskWatch.Core;

public enum FileCategory { Video, Audio, Images, Documents, Archives, Installers, Applications, Code, VirtualMachines, System, Other }

public static class FileCategoryExt
{
    public static string Title(this FileCategory c) => c switch
    {
        FileCategory.Video => "Video",
        FileCategory.Audio => "Audio",
        FileCategory.Images => "Images",
        FileCategory.Documents => "Documents",
        FileCategory.Archives => "Archives",
        FileCategory.Installers => "Disk Images & Installers",
        FileCategory.Applications => "Applications",
        FileCategory.Code => "Code & Developer",
        FileCategory.VirtualMachines => "Virtual Machines",
        FileCategory.System => "System & Libraries",
        _ => "Other",
    };

    /// <summary>One-accent palette: kinds differ by lightness (accent family, then greys), never by a new hue.</summary>
    public static uint Hex(this FileCategory c) => c switch
    {
        FileCategory.Video => 0x3DFFA0,
        FileCategory.Images => 0x17D97A,
        FileCategory.Audio => 0x0E8A4F,
        FileCategory.Documents => 0xF2F2F2,
        FileCategory.Archives => 0xC4C4C4,
        FileCategory.Installers => 0xA6A6A6,
        FileCategory.Applications => 0x8A8A8A,
        FileCategory.Code => 0x6B6B6B,
        FileCategory.VirtualMachines => 0x555555,
        FileCategory.System => 0x444444,
        _ => 0x333333,
    };

    /// <summary>True when text on this fill should be black.</summary>
    public static bool DarkInk(this FileCategory c) => c is FileCategory.Video or FileCategory.Images or FileCategory.Audio
        or FileCategory.Documents or FileCategory.Archives or FileCategory.Installers or FileCategory.Applications;

    private static readonly Dictionary<string, FileCategory> Map = Build();

    private static Dictionary<string, FileCategory> Build()
    {
        var m = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory c, string exts) { foreach (var e in exts.Split(' ')) m[e] = c; }
        Add(FileCategory.Video, "mp4 m4v mov avi mkv wmv flv webm mpg mpeg 3gp mts m2ts ts vob braw r3d mxf hevc");
        Add(FileCategory.Audio, "mp3 m4a aac wav aif aiff flac ogg opus wma alac caf mid midi m4b m4p");
        Add(FileCategory.Images, "jpg jpeg png gif heic heif tif tiff bmp webp raw cr2 cr3 nef arw dng orf rw2 psd psb ai svg ico icns xcf fig exr hdr avif jxl");
        Add(FileCategory.Documents, "pdf doc docx xls xlsx ppt pptx txt rtf md csv odt ods odp epub mobi tex html htm xml json yaml yml log eml msg one pst ost");
        Add(FileCategory.Archives, "zip rar 7z tar gz tgz bz2 xz zst lz4 lzma cab jar war cpio");
        Add(FileCategory.Installers, "msi msix msixbundle appx appxbundle iso img dmg pkg xip esd wim");
        Add(FileCategory.Applications, "exe lnk com scr");
        Add(FileCategory.Code, "cs vb fs cpp cc cxx c h hpp js ts tsx jsx mjs cjs py pyc rb go rs java kt kts scala php pl sh ps1 psm1 bat cmd lua r dart vue svelte css scss less sql o obj lib pdb class wasm ipynb gradle sln csproj vcxproj map lock node nupkg");
        Add(FileCategory.VirtualMachines, "vmdk vdi vhd vhdx qcow2 hdd avhdx vmcx vmrs vsv vmem nvram");
        Add(FileCategory.System, "dll sys drv ocx cpl mui cat inf ini dat db sqlite sqlite3 db-wal db-shm etl evtx cab tmp bin cache pf ttf otf ttc woff woff2 manifest");
        return m;
    }

    public static FileCategory Of(FileNode n)
    {
        if (n.InProgramFiles) return FileCategory.Applications;
        if (n.IsDirectory) return FileCategory.Other;
        var e = n.Extension;
        return e.Length == 0 ? FileCategory.Other : Map.GetValueOrDefault(e, FileCategory.Other);
    }
}

public sealed class CategoryStat
{
    public FileCategory Category;
    public int Count;
    public long Allocated, Logical;
    public long Size(SizeMode m) => m == SizeMode.Allocated ? Allocated : Logical;
}

public sealed class ExtensionStat
{
    public string Ext = "";
    public FileCategory Category;
    public int Count;
    public long Allocated, Logical;
    public long Size(SizeMode m) => m == SizeMode.Allocated ? Allocated : Logical;
}

/// <summary>Flattened, sorted views of a scan, rebuilt in the background after scans and deletions.</summary>
public sealed class DerivedStats
{
    public List<FileNode> Files = new();
    public List<CategoryStat> Categories = new();
    public List<ExtensionStat> Extensions = new();
    public List<FileNode> Unreadable = new();
    public List<FileNode> Excluded = new();
    public int Links;

    public static DerivedStats Build(FileNode root, SizeMode mode)
    {
        var d = new DerivedStats();
        var cats = new Dictionary<FileCategory, CategoryStat>();
        var exts = new Dictionary<string, ExtensionStat>();
        var stack = new Stack<FileNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.IsDirectory)
            {
                if (n.Unreadable) d.Unreadable.Add(n);
                if (n.Excluded) d.Excluded.Add(n);
                if (n.IsLink) d.Links++;
                foreach (var c in n.Children) stack.Push(c);
                continue;
            }
            d.Files.Add(n);
            var cat = FileCategoryExt.Of(n);
            if (!cats.TryGetValue(cat, out var cs)) cats[cat] = cs = new CategoryStat { Category = cat };
            cs.Count++; cs.Allocated += n.Allocated; cs.Logical += n.Logical;
            var e = n.Extension.Length == 0 ? "(none)" : n.Extension;
            if (!exts.TryGetValue(e, out var es)) exts[e] = es = new ExtensionStat { Ext = e, Category = cat };
            es.Count++; es.Allocated += n.Allocated; es.Logical += n.Logical;
        }
        d.Files.Sort((a, b) => b.Size(mode).CompareTo(a.Size(mode)));
        d.Categories = cats.Values.OrderByDescending(c => c.Size(mode)).ToList();
        d.Extensions = exts.Values.OrderByDescending(e => e.Size(mode)).ToList();
        return d;
    }
}

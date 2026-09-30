namespace DiskWatch.Core;

public static class FileOps
{
    public static string LogPath => Path.Join(Platform.LocalAppData, "DiskWatch", "Logs", "deletions.log");

    public static void MoveToRecycleBin(string path)
    {
        if (path == SafetyClassifier.RecycleBinPath) { EmptyRecycleBin(); return; }
        if (!File.Exists(path) && !Directory.Exists(path)) throw new IOException("The item no longer exists.");
        Platform.Recycle(path);
    }

    public static void DeletePermanently(string path)
    {
        if (path == SafetyClassifier.RecycleBinPath) { EmptyRecycleBin(); return; }
        var attr = File.GetAttributes(path);
        if ((attr & FileAttributes.Directory) != 0)
        {
            if ((attr & FileAttributes.ReparsePoint) != 0) { Directory.Delete(path, false); return; } // remove the link, not its target
            var failures = new List<string>();
            DeleteTree(new DirectoryInfo(path), failures);
            if (failures.Count > 0)
                throw new IOException($"{failures.Count} item(s) inside could not be removed (in use or protected), e.g. {failures[0]}");
        }
        else
        {
            if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
    }

    private static void DeleteTree(DirectoryInfo dir, List<string> failures)
    {
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }))
            {
                try
                {
                    if (entry is DirectoryInfo sub && (sub.Attributes & FileAttributes.ReparsePoint) == 0) { DeleteTree(sub, failures); continue; }
                    if ((entry.Attributes & FileAttributes.ReadOnly) != 0) entry.Attributes &= ~FileAttributes.ReadOnly;
                    if (entry is DirectoryInfo link) link.Delete(false); else entry.Delete();
                }
                catch (Exception) { failures.Add(entry.FullName); }
            }
            if ((dir.Attributes & FileAttributes.ReadOnly) != 0) dir.Attributes &= ~FileAttributes.ReadOnly;
            dir.Delete(false);
        }
        catch (Exception) { failures.Add(dir.FullName); }
    }

    private static void EmptyRecycleBin()
    {
        if (!Platform.EmptyRecycleBin()) throw new IOException("Windows couldn't empty the Recycle Bin.");
    }

    public static string Describe(Exception e) => e switch
    {
        UnauthorizedAccessException => "Access denied — the item is protected, read-only, or owned by the system.",
        FileNotFoundException or DirectoryNotFoundException => "The item no longer exists.",
        PathTooLongException => "The path is too long.",
        IOException io when io.HResult == unchecked((int)0x80070020) => "The item is in use by another program.",
        _ => e.Message,
    };

    public static void Log(string action, string path, long size, string detail = "")
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var line = $"{DateTime.Now:O}\t{action}\t{Fmt.Bytes(size)}\t{path}{(detail.Length > 0 ? "\t" + detail : "")}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch { /* logging must never break a delete */ }
    }
}

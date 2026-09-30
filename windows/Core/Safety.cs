namespace DiskWatch.Core;

public enum SafetyLevel { Safe = 0, Caution = 1, Danger = 2, Protected = 3 }

public static class SafetyLevelExt
{
    public static string Title(this SafetyLevel l) => l switch
    {
        SafetyLevel.Safe => "Safe to remove",
        SafetyLevel.Caution => "Review first",
        SafetyLevel.Danger => "Dangerous",
        _ => "Protected",
    };

    public static string Short(this SafetyLevel l) => l switch
    {
        SafetyLevel.Safe => "Safe",
        SafetyLevel.Caution => "Caution",
        SafetyLevel.Danger => "Danger",
        _ => "Protected",
    };

    public static string Explanation(this SafetyLevel l) => l switch
    {
        SafetyLevel.Safe => "Caches, logs, downloads and similar data that can be removed without harming Windows.",
        SafetyLevel.Caution => "Your files or app data. Removing is allowed, but make sure you no longer need it.",
        SafetyLevel.Danger => "System-wide or sensitive data. Removing it can break apps, sync, or Windows features. You must type DELETE to confirm.",
        _ => "Required by Windows or owned by the system. DiskWatch will not remove it.",
    };
}

public sealed record SafetyVerdict(SafetyLevel Level, IReadOnlyList<string> Reasons);

/// <summary>Classifies Windows paths into four safety levels before anything may be removed.</summary>
public static class SafetyClassifier
{
    public const string RecycleBinPath = "::RecycleBin";
    private static readonly StringComparison IC = StringComparison.OrdinalIgnoreCase;
    private static readonly char Sep = Path.DirectorySeparatorChar;

    private static string Norm(string p)
    {
        try { p = Path.GetFullPath(p); } catch { }
        if (p.Length > 3) p = p.TrimEnd(Sep);
        return p;
    }

    private static bool Under(string p, string prefix) => p.Equals(prefix, IC) || StrictlyUnder(p, prefix);
    private static bool StrictlyUnder(string p, string prefix) =>
        prefix.Length > 0 && p.StartsWith(prefix.EndsWith(Sep) ? prefix : prefix + Sep, IC);

    public static SafetyVerdict Classify(FileNode n) => Classify(n.FullPath, n.Attributes);

    public static SafetyVerdict Classify(string path)
    {
        if (path == RecycleBinPath) return new(SafetyLevel.Safe, new[] { "Already in the Recycle Bin." });
        FileAttributes attr = 0;
        try { attr = File.GetAttributes(path); } catch { }
        return Classify(path, attr);
    }

    public static SafetyVerdict Classify(string rawPath, FileAttributes attr)
    {
        if (rawPath == RecycleBinPath) return new(SafetyLevel.Safe, new[] { "Already in the Recycle Bin." });
        var p = Norm(rawPath);
        var level = SafetyLevel.Safe;
        var reasons = new List<string>();
        void Raise(SafetyLevel l, string r)
        {
            if (l > level) level = l;
            if (!reasons.Contains(r)) reasons.Add(r);
        }

        string home = Platform.Home, win = Platform.WindowsDir, pf = Platform.ProgramFiles, pf86 = Platform.ProgramFilesX86;
        string pd = Platform.ProgramData, users = Platform.UsersDir, local = Platform.LocalAppData, roaming = Platform.RoamingAppData;
        string drive = Path.GetPathRoot(p) ?? Platform.SystemDrive;
        string name = Path.GetFileName(p);

        // --- Hard blocks -------------------------------------------------
        var critical = new[]
        {
            win, pf, pf86, pd, users, Path.Join(users, "Public"), Path.Join(users, "Default"), Path.Join(users, "All Users"),
            home, local, roaming, Path.Join(home, "AppData"), Path.Join(home, "AppData", "LocalLow"),
            Path.Join(home, "Desktop"), Path.Join(home, "Documents"), Path.Join(home, "Downloads"), Path.Join(home, "Pictures"),
            Path.Join(home, "Music"), Path.Join(home, "Videos"), Path.Join(home, "OneDrive"), Platform.TempDir,
            Path.Join(win, "Temp"), Path.Join(local, "Temp"), Path.Join(local, "Packages"), Path.Join(local, "Microsoft"),
            Path.Join(roaming, "Microsoft"),
        }.Where(s => s.Length > 0).Select(Norm).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Platform.IsVolumeRoot(p)) Raise(SafetyLevel.Protected, "This is the root of a drive.");
        if (critical.Contains(p)) Raise(SafetyLevel.Protected, "Essential top-level folder. Clean what's inside it instead of removing the folder itself.");

        if (Under(p, win))
        {
            var allowed = new[] { "Temp", @"SoftwareDistribution\Download", "Logs", "Minidump", "Prefetch", "Downloaded Program Files", @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache" };
            if (!allowed.Any(a => StrictlyUnder(p, Path.Join(win, a))) && !p.Equals(Path.Join(win, "MEMORY.DMP"), IC))
                Raise(SafetyLevel.Protected, "Part of Windows itself. Use Disk Cleanup or Storage Sense for Windows files.");
        }
        var rootFiles = new[] { "pagefile.sys", "hiberfil.sys", "swapfile.sys", "bootmgr", "BOOTNXT", "DumpStack.log.tmp" };
        if (Path.GetDirectoryName(p) is string dir && Platform.IsVolumeRoot(dir) && rootFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
            Raise(SafetyLevel.Protected, "Managed by Windows (virtual memory, hibernation or boot). Change it in System settings instead.");
        foreach (var sys in new[] { "System Volume Information", "$Recycle.Bin", "Recovery", "Boot", "EFI", "$WinREAgent", "$SysReset", "$Windows.~BT", "$Windows.~WS", "Config.Msi" })
            if (Under(p, Path.Join(drive, sys))) Raise(SafetyLevel.Protected, "Hidden Windows system area on this drive.");
        var protectedAreas = new (string Path, string Why)[]
        {
            (Path.Join(pf, "WindowsApps"), "Microsoft Store apps — remove them from Settings → Apps."),
            (Path.Join(pf, "Windows Defender"), "Windows Security (antivirus)."),
            (Path.Join(pf, "Common Files", "microsoft shared"), "Shared Microsoft components."),
            (Path.Join(pd, "Microsoft", "Crypto"), "System keys and certificates."),
            (Path.Join(pd, "Microsoft", "Windows Defender"), "Windows Security data."),
            (Path.Join(roaming, "Microsoft", "Credentials"), "Saved Windows credentials."),
            (Path.Join(roaming, "Microsoft", "Protect"), "Encryption keys protecting your saved passwords."),
            (Path.Join(roaming, "Microsoft", "Crypto"), "Your certificates and keys."),
            (Path.Join(local, "Microsoft", "Credentials"), "Saved Windows credentials."),
        };
        foreach (var (prefix, why) in protectedAreas) if (Under(p, prefix)) Raise(SafetyLevel.Protected, why);
        if (Path.GetDirectoryName(p) is string d2 && d2.Equals(home, IC) && name.StartsWith("NTUSER", IC))
            Raise(SafetyLevel.Protected, "Your registry hive — Windows can't sign you in without it.");
        if (Platform.ExePath.Length > 0 && Under(Platform.ExePath, p) && !Platform.IsVolumeRoot(p))
            Raise(SafetyLevel.Protected, "Contains DiskWatch itself.");
        if (Path.GetDirectoryName(p) is string d3 && d3.Equals(users, IC) && !p.Equals(home, IC))
            Raise(SafetyLevel.Protected, "Another user's entire profile. Remove accounts in Settings → Accounts instead.");

        // --- Dangerous ---------------------------------------------------
        var danger = new (string Path, string Why)[]
        {
            (Path.Join(drive, "Windows.old"), "Previous Windows installation — needed to roll back an upgrade."),
            (pd, "Shared app data for all users."),
            (Path.Join(home, "OneDrive"), "OneDrive — deleting here removes it from the cloud and all your devices."),
            (Path.Join(home, "Dropbox"), "Dropbox — deletions sync everywhere."),
            (Path.Join(home, "Google Drive"), "Google Drive — deletions sync everywhere."),
            (Path.Join(home, "iCloudDrive"), "iCloud Drive — deletions sync everywhere."),
            (Path.Join(home, ".ssh"), "SSH keys — losing them can lock you out of servers."),
            (Path.Join(home, ".gnupg"), "GPG keys."),
            (Path.Join(home, ".aws"), "Cloud credentials."),
            (Path.Join(home, ".kube"), "Kubernetes credentials."),
            (Path.Join(roaming, "Apple Computer", "MobileSync", "Backup"), "iPhone/iPad backups — may be your only copy."),
            (Path.Join(local, "Apple Computer", "MobileSync", "Backup"), "iPhone/iPad backups — may be your only copy."),
            (Path.Join(local, "Packages", "CanonicalGroupLimited"), "WSL Linux distribution data."),
        };
        foreach (var (prefix, why) in danger) if (StrictlyUnder(p, prefix) || (Under(p, prefix) && !critical.Contains(p))) Raise(SafetyLevel.Danger, why);
        var ext = Path.GetExtension(p).ToLowerInvariant();
        if (ext is ".pst" or ".ost") Raise(SafetyLevel.Danger, "Outlook mailbox data.");
        if (ext is ".vhdx" or ".vhd" && p.Contains("wsl", IC)) Raise(SafetyLevel.Danger, "WSL Linux disk image.");
        if (ext is ".kdbx" or ".wallet") Raise(SafetyLevel.Danger, "Password or wallet file.");
        if (StrictlyUnder(p, users) && !Under(p, home) && !Under(p, Path.Join(users, "Public")))
            Raise(SafetyLevel.Danger, "Belongs to another user account.");

        // --- Caution -----------------------------------------------------
        if (StrictlyUnder(p, pf) || StrictlyUnder(p, pf86))
            Raise(SafetyLevel.Caution, "Installed program. Uninstall it from Settings → Apps so its registry entries go too.");
        if (StrictlyUnder(p, roaming) || StrictlyUnder(p, local)) Raise(SafetyLevel.Caution, "App data — settings, databases or documents an app relies on.");
        foreach (var f in new[] { "Documents", "Desktop", "Pictures", "Music", "Videos" })
            if (StrictlyUnder(p, Path.Join(home, f))) Raise(SafetyLevel.Caution, "Your personal files.");
        if (p.Split(Sep).Contains(".git", StringComparer.OrdinalIgnoreCase)) Raise(SafetyLevel.Caution, "Git repository history.");
        if ((attr & FileAttributes.System) != 0 && level < SafetyLevel.Caution) Raise(SafetyLevel.Caution, "Marked as a system file.");

        // --- Known-safe data (only lowers caution → safe) -------------------
        if (level < SafetyLevel.Protected)
        {
            var comps = p.Split(Sep);
            bool isCache = comps.Any(c => c.Equals("Cache", IC) || c.Equals("Caches", IC) || c.Equals("cache2", IC) || c.Equals("Code Cache", IC)
                                          || c.Equals("GPUCache", IC) || c.Equals("D3DSCache", IC) || c.Equals("INetCache", IC)
                                          || c.Equals("ShaderCache", IC) || c.Equals("DXCache", IC) || c.Equals("npm-cache", IC));
            bool isLog = comps.Any(c => c.Equals("Logs", IC) || c.Equals("CrashDumps", IC) || c.Equals("Minidump", IC) || c.Equals("WER", IC))
                         || p.Equals(Path.Join(win, "MEMORY.DMP"), IC);
            bool isTemp = StrictlyUnder(p, Platform.TempDir) || StrictlyUnder(p, Path.Join(local, "Temp")) || StrictlyUnder(p, Path.Join(win, "Temp"));
            bool isDownload = StrictlyUnder(p, Path.Join(home, "Downloads"));
            if (isCache || isLog || isTemp || isDownload)
            {
                string why = isCache ? "Cache data — apps rebuild it automatically."
                    : isLog ? "Logs and crash dumps — only used for troubleshooting."
                    : isTemp ? "Temporary files."
                    : "Downloaded file — usually safe once installed or saved elsewhere.";
                bool systemOwned = Under(p, win) || StrictlyUnder(p, pd);
                if (level <= SafetyLevel.Caution && !systemOwned)
                {
                    level = SafetyLevel.Safe;
                    reasons.Clear();
                    reasons.Add(why);
                }
                else if (systemOwned)
                {
                    level = SafetyLevel.Caution;
                    reasons.Clear();
                    reasons.Add(why);
                    reasons.Add("System-owned; close running apps before removing.");
                }
            }
            if (Under(p, win) && level < SafetyLevel.Protected && reasons.Count == 0)
            {
                level = SafetyLevel.Caution;
                reasons.Add("Windows maintenance data that Windows re-creates as needed.");
            }
        }

        if (level == SafetyLevel.Safe && reasons.Count == 0)
        {
            // Nothing marks it as disposable, so treat it as the user's own data.
            level = SafetyLevel.Caution;
            reasons.Add("Your own files — nothing marks this as disposable, so make sure you no longer need it.");
        }
        if (reasons.Count == 0) reasons.Add(level.Explanation());
        return new SafetyVerdict(level, reasons);
    }
}

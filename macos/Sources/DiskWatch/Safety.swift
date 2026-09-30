import SwiftUI

enum SafetyLevel: Int, Comparable, CaseIterable, Identifiable {
    case safe = 0, caution, danger, protected

    var id: Int { rawValue }
    static func < (a: SafetyLevel, b: SafetyLevel) -> Bool { a.rawValue < b.rawValue }

    var title: String {
        switch self {
        case .safe: "Safe to remove"
        case .caution: "Review first"
        case .danger: "Dangerous"
        case .protected: "Protected"
        }
    }

    var shortTitle: String {
        switch self {
        case .safe: "Safe"
        case .caution: "Caution"
        case .danger: "Danger"
        case .protected: "Protected"
        }
    }

    var color: Color {
        switch self {
        case .safe: FN.accent
        case .caution: FN.fg
        case .danger: FN.warn
        case .protected: FN.danger
        }
    }

    var nsColor: NSColor {
        switch self {
        case .safe: FN.NS.accent
        case .caution: FN.NS.fg
        case .danger: FN.NS.warn
        case .protected: FN.NS.danger
        }
    }

    /// Maps onto the design system's pill scale (protected is the one filled, critical state).
    var pill: FNPillState {
        switch self {
        case .safe: .ok
        case .caution: .normal
        case .danger: .warn
        case .protected: .danger
        }
    }

    var symbol: String {
        switch self {
        case .safe: "checkmark.shield.fill"
        case .caution: "exclamationmark.triangle.fill"
        case .danger: "exclamationmark.octagon.fill"
        case .protected: "lock.shield.fill"
        }
    }

    var explanation: String {
        switch self {
        case .safe: "Caches, logs, downloads and similar data that can be removed without harming macOS."
        case .caution: "Your files or app data. Removing is allowed, but make sure you no longer need it."
        case .danger: "System-wide or sensitive data. Removing it can break apps, sync, or macOS features. You must type DELETE to confirm."
        case .protected: "Required by macOS or protected by System Integrity Protection. DiskWatch will not remove it."
        }
    }
}

struct SafetyVerdict {
    var level: SafetyLevel
    var reasons: [String]
}

enum SafetyClassifier {
    private static let SF_RESTRICTED_FLAG: UInt32 = 0x0008_0000
    private static let IMMUTABLE_FLAGS: UInt32 = 0x0000_0002 | 0x0002_0000 // UF_IMMUTABLE | SF_IMMUTABLE

    static func normalize(_ path: String) -> String {
        var p = path
        if p.hasPrefix("/System/Volumes/Data/") { p = String(p.dropFirst("/System/Volumes/Data".count)) }
        for (a, b) in [("/etc", "/private/etc"), ("/var", "/private/var"), ("/tmp", "/private/tmp")] {
            if p == a || p.hasPrefix(a + "/") { p = b + p.dropFirst(a.count) }
        }
        while p.count > 1 && p.hasSuffix("/") { p.removeLast() }
        return p
    }

    private static func under(_ p: String, _ prefix: String) -> Bool {
        p == prefix || p.hasPrefix(prefix + "/")
    }

    private static func strictlyUnder(_ p: String, _ prefix: String) -> Bool {
        p.hasPrefix(prefix + "/")
    }

    static let mountPoints: Set<String> = Set(MountInfo.all().map(\.path))

    static func classify(node: FileNode) -> SafetyVerdict {
        classify(path: node.path, flags: node.flags, uid: node.uid)
    }

    static func classify(path: String) -> SafetyVerdict {
        var st = stat()
        if lstat(path, &st) == 0 {
            return classify(path: path, flags: st.st_flags, uid: st.st_uid)
        }
        return classify(path: path, flags: 0, uid: RealUser.uid)
    }

    static func classify(path rawPath: String, flags: UInt32, uid: UInt32) -> SafetyVerdict {
        let p = normalize(rawPath)
        let home = RealUser.home
        var level = SafetyLevel.safe
        var reasons: [String] = []
        func raise(_ l: SafetyLevel, _ r: String) {
            if l > level { level = l }
            if !reasons.contains(r) { reasons.append(r) }
        }

        // --- Hard blocks -------------------------------------------------
        if flags & SF_RESTRICTED_FLAG != 0 {
            raise(.protected, "Protected by System Integrity Protection (SIP). Even root cannot remove it.")
        }
        if flags & IMMUTABLE_FLAGS != 0 {
            raise(.protected, "Marked as locked/immutable. Unlock it in Finder's Get Info first if you really mean to remove it.")
        }
        let critical: Set<String> = [
            "/", "/Applications", "/Applications/Utilities", "/Library", "/System", "/Users", "/Users/Shared",
            "/Volumes", "/private", "/private/var", "/private/tmp", "/private/etc", "/usr", "/usr/local", "/opt",
            "/cores", "/bin", "/sbin", "/dev",
            "/Library/Caches", "/Library/Logs", "/Library/Application Support", "/Library/LaunchDaemons",
            "/Library/LaunchAgents", "/Library/Preferences", "/Library/Frameworks", "/Library/Extensions",
            "/private/var/log", "/private/var/folders",
            home, home + "/Library", home + "/Library/Caches", home + "/Library/Logs",
            home + "/Library/Application Support", home + "/Library/Preferences", home + "/Library/Containers",
            home + "/Library/Group Containers", home + "/Desktop", home + "/Documents", home + "/Downloads",
            home + "/Movies", home + "/Music", home + "/Pictures", home + "/Public", home + "/.Trash",
            home + "/Applications", home + "/Library/Mobile Documents"
        ]
        if critical.contains(p) {
            raise(.protected, "Essential top-level folder. Clean what's inside it instead of removing the folder itself.")
        }
        let protectedAreas: [(String, String)] = [
            ("/System", "Part of the macOS system (sealed, read-only volume)."),
            ("/bin", "Core Unix commands macOS needs to boot."),
            ("/sbin", "Core system administration commands."),
            ("/private/var/db", "System databases (users, Spotlight, security, updates)."),
            ("/private/var/vm", "Virtual memory swap and sleep image — managed by macOS."),
            ("/private/var/protected", "Protected system data."),
            ("/private/etc", "System configuration files (network, users, sudo)."),
            ("/Library/Apple", "Apple-installed system components."),
            ("/Library/Keychains", "System keychain (certificates and passwords)."),
            ("/Library/Security", "System security configuration."),
            ("/Library/Preferences/SystemConfiguration", "Network and system configuration."),
            (home + "/Library/Keychains", "Your keychain — all saved passwords and certificates."),
        ]
        for (prefix, why) in protectedAreas where under(p, prefix) {
            raise(.protected, why)
        }
        if under(p, "/usr") && !under(p, "/usr/local") {
            raise(.protected, "Core Unix system files provided by macOS.")
        }
        if mountPoints.contains(rawPath) || mountPoints.contains(p) {
            raise(.protected, "This is the root of a mounted volume.")
        }
        if let bundle = Bundle.main.bundlePath as String?, under(p, bundle), bundle.hasSuffix(".app") {
            raise(.protected, "This is DiskWatch itself.")
        }
        // Another user's home folder itself
        let parent = (p as NSString).deletingLastPathComponent
        if parent == "/Users" && p != home && p != "/Users/Shared" {
            raise(.protected, "Another user's entire home folder. Remove users in System Settings instead.")
        }

        // --- Dangerous ---------------------------------------------------
        let dangerAreas: [(String, String)] = [
            ("/Library/LaunchDaemons", "Background services that start with macOS."),
            ("/Library/LaunchAgents", "Background agents that start for every user."),
            ("/Library/Extensions", "Kernel/driver extensions — removing can break hardware support."),
            ("/Library/PrivilegedHelperTools", "Privileged helpers used by installed apps."),
            ("/Library/Frameworks", "Shared frameworks used by installed apps."),
            ("/Library/Application Support", "System-wide app data shared by all users."),
            ("/Library/Preferences", "System-wide preferences."),
            ("/Library/Filesystems", "File-system drivers."),
            ("/Library/Audio", "Audio drivers and plug-ins."),
            ("/Library/Printers", "Printer drivers."),
            ("/private/var", "System runtime data."),
            (home + "/Library/Mobile Documents", "iCloud Drive — deleting here removes it from ALL your devices."),
            (home + "/Library/Mail", "Your Mail messages and mailboxes."),
            (home + "/Library/Messages", "Your Messages history and attachments."),
            (home + "/Library/Application Support/MobileSync", "iPhone/iPad backups — may be your only copy."),
            (home + "/Library/CloudStorage", "Cloud-synced folder (Dropbox, Google Drive, OneDrive…) — deletions sync everywhere."),
            (home + "/.ssh", "SSH keys — losing them can lock you out of servers."),
            (home + "/.gnupg", "GPG keys."),
            (home + "/.aws", "Cloud credentials."),
            (home + "/.kube", "Kubernetes credentials."),
        ]
        for (prefix, why) in dangerAreas where strictlyUnder(p, prefix) || (under(p, prefix) && !critical.contains(p)) {
            raise(.danger, why)
        }
        if strictlyUnder(p, "/Library") && !strictlyUnder(p, "/Library/Caches") && !strictlyUnder(p, "/Library/Logs") && level < .danger {
            raise(.danger, "System-wide library shared by all users.")
        }
        if p.contains(".photoslibrary") { raise(.danger, "Photos library — may contain your only copy of photos.") }
        if p.contains("Backups.backupdb") || p.hasSuffix(".backupbundle") || p.contains(".timemachine") {
            raise(.danger, "Time Machine backup data.")
        }
        if strictlyUnder(p, "/Users") && !under(p, home) && !under(p, "/Users/Shared") {
            raise(.danger, "Belongs to another user account.")
        }

        // --- Caution -----------------------------------------------------
        if strictlyUnder(p, "/Applications") {
            raise(.caution, "Application. If it came with an uninstaller, use that to remove helpers too.")
        }
        if strictlyUnder(p, "/usr/local") || strictlyUnder(p, "/opt") {
            raise(.caution, "Command-line tools (Homebrew, etc.).")
        }
        if strictlyUnder(p, home + "/Library/Application Support") { raise(.caution, "App data — settings, databases or documents an app relies on.") }
        if strictlyUnder(p, home + "/Library/Preferences") { raise(.caution, "App preferences (settings will reset).") }
        if strictlyUnder(p, home + "/Library/Containers") || strictlyUnder(p, home + "/Library/Group Containers") {
            raise(.caution, "Sandboxed app data — may contain documents or settings.")
        }
        if strictlyUnder(p, home + "/Library/LaunchAgents") { raise(.caution, "Login item / background agent.") }
        if p.split(separator: "/").contains(".git") { raise(.caution, "Git repository history.") }
        for folder in ["Documents", "Desktop", "Pictures", "Movies", "Music"] where strictlyUnder(p, home + "/" + folder) {
            raise(.caution, "Your personal files.")
        }
        if uid == 0 && level < .caution && !strictlyUnder(p, home) {
            raise(.caution, "Owned by the system (root).")
        } else if uid != RealUser.uid && uid != 0 && level < .caution {
            raise(.caution, "Owned by another account.")
        }

        // --- Known-safe data (only lowers caution → safe) ---------------------
        if level <= .danger && level != .protected {
            let comps = p.split(separator: "/")
            let isCache = comps.contains("Caches") || comps.contains("DerivedData") || comps.contains("CachedData")
                || comps.contains("Cache") || comps.contains("Code Cache")
            let isLog = comps.contains("Logs") || strictlyUnder(p, "/private/var/log")
            let safeRoots = [home + "/.Trash", home + "/Downloads", home + "/Library/Developer/Xcode/DerivedData",
                             home + "/Library/Developer/Xcode/iOS DeviceSupport", home + "/Library/Developer/CoreSimulator/Caches",
                             home + "/.npm", home + "/.gradle/caches", "/private/tmp", "/private/var/tmp"]
            if isCache || isLog || safeRoots.contains(where: { strictlyUnder(p, $0) }) {
                let why = isCache ? "Cache data — apps rebuild it automatically."
                    : isLog ? "Log files — only used for troubleshooting."
                    : strictlyUnder(p, home + "/Downloads") ? "Downloaded file — usually safe once installed or saved elsewhere."
                    : strictlyUnder(p, home + "/.Trash") ? "Already in the Trash."
                    : "Temporary/disposable data."
                if level <= .caution {
                    level = .safe
                    reasons.removeAll()
                    reasons.append(why)
                } else if strictlyUnder(p, "/private/var/log") || strictlyUnder(p, "/Library/Caches") || strictlyUnder(p, "/private/tmp") || strictlyUnder(p, "/private/var/tmp") {
                    level = .caution
                    reasons = [why, "System-owned; quit running apps before removing."]
                }
            }
        }
        if level == .safe && reasons.isEmpty {
            // Nothing marks it as disposable, so treat it as the user's own data.
            level = .caution
            reasons.append("Your own files — nothing marks this as disposable, so make sure you no longer need it.")
        }
        if reasons.isEmpty { reasons.append(level.explanation) }
        return SafetyVerdict(level: level, reasons: reasons)
    }
}

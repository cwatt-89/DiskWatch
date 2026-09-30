import Foundation

/// `DiskWatch --selftest [path]` — exercises the scanner and safety rules without any UI.
enum SelfTest {
    static func run() {
        let args = CommandLine.arguments
        let path = args.firstIndex(of: "--selftest").flatMap { i in i + 1 < args.count ? args[i + 1] : nil } ?? RealUser.home
        print("user=\(RealUser.name) uid=\(RealUser.uid) home=\(RealUser.home) root=\(RealUser.isRoot) fda=\(FullDiskAccess.granted)")
        let start = Date()
        let s = DiskScanner()
        guard let root = s.scan(rootPath: path) else { print("scan failed"); return }
        let t = Date().timeIntervalSince(start)
        let p = s.progress.snapshot()
        print(String(format: "scanned %@ in %.2fs: %d files, %d dirs, %d errors", path, t, root.fileCount, root.dirCount, p.errors))
        print("allocated=\(root.allocatedSize) (\(Fmt.bytes(root.allocatedSize))) logical=\(Fmt.bytes(root.logicalSize))")
        for c in root.children.prefix(8) {
            print("  \(Fmt.bytes(c.allocatedSize).padding(toLength: 10, withPad: " ", startingAt: 0)) \(c.name)")
        }
        let d = DerivedStats.build(root: root, mode: .allocated)
        print("categories: " + d.categories.prefix(5).map { "\($0.category.title)=\(Fmt.bytes($0.allocated))" }.joined(separator: ", "))
        print("unreadable dirs: \(d.unreadable.count), excluded: \(d.excluded.count), hardlink dups: \(d.hardlinks)")
        if let f = d.files.first, let found = root.find(path: f.path) { print("find() ok: \(found === f) \(f.path)") }

        let h = RealUser.home
        let samples = ["/", "/System/Library/CoreServices", "/usr/bin/ls", "/usr/local/bin", "/private/etc/hosts", "/etc/sudoers",
                       "/Library/Caches/x", "/Library/LaunchDaemons/x.plist", "/private/var/log/system.log.0.gz", "/private/var/db/x",
                       "/Applications/Safari.app", "/Applications/Some.app", h, h + "/Library", h + "/Library/Caches/com.foo",
                       h + "/Library/Keychains/login.keychain-db", h + "/Library/Mobile Documents/x", h + "/Downloads/a.dmg",
                       h + "/Documents/report.pdf", h + "/.ssh/id_ed25519", h + "/Library/Application Support/Foo",
                       h + "/Library/Developer/Xcode/DerivedData/Proj", "/System/Volumes/Data/Users/x", "/Users/someoneelse",
                       h + "/.Trash/old.zip"]
        print("\nsafety:")
        for sp in samples {
            let v = SafetyClassifier.classify(path: sp)
            print("  \(v.level.shortTitle.padding(toLength: 9, withPad: " ", startingAt: 0)) \(sp)  — \(v.reasons.first ?? "")")
        }
    }
}

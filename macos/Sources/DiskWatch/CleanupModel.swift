import Foundation
import Observation

@MainActor
@Observable
final class CleanupModel {
    enum Section: String, CaseIterable, Identifiable {
        case system = "System Junk"
        case user = "User Caches & Logs"
        case developer = "Developer"
        case apps = "App Caches"
        case downloads = "Downloads & Installers"
        case personal = "Large Personal Data (review carefully)"
        var id: String { rawValue }
    }

    enum Target {
        case contents(String)
        case olderThan(String, days: Int)
        case matching(String, Set<String>)
        case rotatedLogs(String)
    }

    struct Item: Identifiable {
        let id: String
        let title: String
        let detail: String
        let symbol: String
        let section: Section
        let safety: SafetyLevel
        let targets: [Target]
        var permanentOnly = false
        var defaultChecked = true
    }

    struct Measure {
        var size: Int64 = 0
        var count = 0
        var paths: [(path: String, size: Int64)] = []
    }

    var items: [Item] = CleanupModel.catalog()
    var measures: [String: Measure] = [:]
    var measuring: Set<String> = []
    var checked: Set<String> = []
    var hasScanned = false
    var snapshots: [String] = []
    var snapshotMessage: String?

    var isMeasuring: Bool { !measuring.isEmpty }

    var visibleItems: [Item] {
        guard hasScanned else { return items }
        return items.filter { (measures[$0.id]?.count ?? 0) > 0 || measuring.contains($0.id) }
    }

    var selectedSize: Int64 {
        items.filter { checked.contains($0.id) }.reduce(0) { $0 + (measures[$1.id]?.size ?? 0) }
    }

    var totalFound: Int64 { measures.values.reduce(0) { $0 + $1.size } }

    func scanAll() {
        hasScanned = true
        measures = [:]
        let all = items
        measuring = Set(all.map(\.id))
        checked = []
        for item in all {
            Task.detached(priority: .utility) { [weak self] in
                let m = CleanupModel.measure(item.targets)
                await MainActor.run {
                    guard let self else { return }
                    self.measures[item.id] = m
                    self.measuring.remove(item.id)
                    if item.defaultChecked && item.safety == .safe && m.count > 0 { self.checked.insert(item.id) }
                }
            }
        }
        loadSnapshots()
    }

    func refreshSizes() {
        guard hasScanned else { return }
        for item in items {
            Task.detached(priority: .utility) { [weak self] in
                let m = CleanupModel.measure(item.targets)
                await MainActor.run { self?.measures[item.id] = m }
            }
        }
    }

    func targets(for ids: Set<String>) -> [(path: String, size: Int64)] {
        items.filter { ids.contains($0.id) }.flatMap { measures[$0.id]?.paths ?? [] }
    }

    func anyPermanentOnly(_ ids: Set<String>) -> Bool {
        items.contains { ids.contains($0.id) && $0.permanentOnly }
    }

    // MARK: Time Machine local snapshots

    func loadSnapshots() {
        Task.detached {
            let r = shell("/usr/bin/tmutil", ["listlocalsnapshots", "/"])
            let snaps = r.out.split(separator: "\n").map(String.init).filter { $0.hasPrefix("com.apple.TimeMachine.") }
            await MainActor.run { self.snapshots = snaps }
        }
    }

    func deleteSnapshot(_ name: String) {
        // com.apple.TimeMachine.2026-09-29-101010.local -> 2026-09-29-101010
        let date = name.replacingOccurrences(of: "com.apple.TimeMachine.", with: "").replacingOccurrences(of: ".local", with: "")
        Task.detached {
            let r = shell("/usr/bin/tmutil", ["deletelocalsnapshots", date])
            FileOps.log(r.status == 0 ? "SNAPSHOT" : "FAILED", path: name, size: 0, detail: r.out.trimmingCharacters(in: .whitespacesAndNewlines))
            await MainActor.run {
                self.snapshotMessage = r.status == 0 ? "Deleted snapshot \(date)." : "Could not delete \(date): \(r.out)"
                self.loadSnapshots()
            }
        }
    }

    // MARK: Measuring

    nonisolated static func measure(_ targets: [Target]) -> Measure {
        var m = Measure()
        let fm = FileManager.default
        for t in targets {
            switch t {
            case .contents(let dir):
                guard fm.fileExists(atPath: dir), let node = DiskScanner().scan(rootPath: dir) else { continue }
                for c in node.children where c.allocatedSize > 0 || c.isDirectory {
                    m.paths.append((dir + "/" + c.name, c.allocatedSize))
                    m.size += c.allocatedSize
                    m.count += c.isDirectory ? max(1, c.fileCount) : 1
                }
            case .olderThan(let dir, let days):
                guard fm.fileExists(atPath: dir), let node = DiskScanner().scan(rootPath: dir) else { continue }
                let cutoff = Date().timeIntervalSince1970 - Double(days) * 86400
                for c in node.children where c.modified < cutoff && !c.name.hasPrefix(".") {
                    m.paths.append((dir + "/" + c.name, c.allocatedSize))
                    m.size += c.allocatedSize
                    m.count += 1
                }
            case .matching(let dir, let exts):
                guard fm.fileExists(atPath: dir), let node = DiskScanner().scan(rootPath: dir) else { continue }
                for c in node.children where exts.contains(c.nameExtension) {
                    m.paths.append((dir + "/" + c.name, c.allocatedSize))
                    m.size += c.allocatedSize
                    m.count += 1
                }
            case .rotatedLogs(let dir):
                guard fm.fileExists(atPath: dir), let node = DiskScanner().scan(rootPath: dir) else { continue }
                var stack = [(node, dir)]
                while let (n, p) = stack.popLast() {
                    for c in n.children {
                        let cp = p + "/" + c.name
                        if c.isDirectory { stack.append((c, cp)); continue }
                        let e = c.nameExtension
                        if ["gz", "bz2", "xz", "zip", "old"].contains(e) || Int(e) != nil {
                            m.paths.append((cp, c.allocatedSize))
                            m.size += c.allocatedSize
                            m.count += 1
                        }
                    }
                }
            }
        }
        m.paths.sort { $0.size > $1.size }
        return m
    }

    // MARK: Catalog

    static func catalog() -> [Item] {
        let h = RealUser.home
        let lib = h + "/Library"
        let appSup = lib + "/Application Support"
        return [
            Item(id: "userCaches", title: "User caches", detail: "Temporary data apps keep to speed things up. Apps rebuild it as needed — quit apps first for best results.",
                 symbol: "tray.full", section: .user, safety: .safe, targets: [.contents(lib + "/Caches")]),
            Item(id: "userLogs", title: "User logs", detail: "Diagnostic logs written by your apps.",
                 symbol: "doc.plaintext", section: .user, safety: .safe, targets: [.contents(lib + "/Logs")]),
            Item(id: "trash", title: "Trash", detail: "Items already in your Trash. Removing them here is permanent.",
                 symbol: "trash", section: .user, safety: .safe, targets: [.contents(h + "/.Trash")], permanentOnly: true),
            Item(id: "savedState", title: "Saved application state", detail: "Window positions apps restore on relaunch.",
                 symbol: "macwindow.on.rectangle", section: .user, safety: .safe, targets: [.contents(lib + "/Saved Application State")], defaultChecked: false),

            Item(id: "sysCaches", title: "System caches", detail: "System-wide caches in /Library/Caches. Rebuilt automatically.",
                 symbol: "internaldrive", section: .system, safety: .caution, targets: [.contents("/Library/Caches")], defaultChecked: false),
            Item(id: "sysLogs", title: "System logs", detail: "/Library/Logs diagnostic data.",
                 symbol: "doc.text.magnifyingglass", section: .system, safety: .caution, targets: [.contents("/Library/Logs")], defaultChecked: false),
            Item(id: "rotatedLogs", title: "Old rotated logs", detail: "Compressed/archived logs in /private/var/log that macOS already rotated out.",
                 symbol: "archivebox", section: .system, safety: .caution, targets: [.rotatedLogs("/private/var/log")], defaultChecked: false),
            Item(id: "diag", title: "Crash & diagnostic reports", detail: "Crash reports and spin dumps.",
                 symbol: "exclamationmark.bubble", section: .system, safety: .safe,
                 targets: [.contents(lib + "/Logs/DiagnosticReports"), .contents("/Library/Logs/DiagnosticReports")]),
            Item(id: "tmp", title: "Old temporary files", detail: "Items in /private/tmp and /private/var/tmp untouched for 3+ days.",
                 symbol: "clock.arrow.circlepath", section: .system, safety: .caution,
                 targets: [.olderThan("/private/tmp", days: 3), .olderThan("/private/var/tmp", days: 3)], defaultChecked: false),
            Item(id: "updates", title: "Downloaded macOS updates", detail: "Leftover software update packages in /Library/Updates.",
                 symbol: "arrow.down.circle", section: .system, safety: .caution, targets: [.contents("/Library/Updates")], defaultChecked: false),

            Item(id: "derived", title: "Xcode DerivedData", detail: "Build products and indexes. Xcode regenerates them on the next build.",
                 symbol: "hammer", section: .developer, safety: .safe, targets: [.contents(lib + "/Developer/Xcode/DerivedData")]),
            Item(id: "deviceSupport", title: "Device support files", detail: "Debug symbols for iOS/watchOS/tvOS/visionOS versions. Re-downloaded when you connect a device.",
                 symbol: "iphone", section: .developer, safety: .safe,
                 targets: ["iOS", "watchOS", "tvOS", "visionOS"].map { .contents(lib + "/Developer/Xcode/\($0) DeviceSupport") }),
            Item(id: "simCaches", title: "Simulator caches", detail: "CoreSimulator caches.",
                 symbol: "ipad.and.iphone", section: .developer, safety: .safe, targets: [.contents(lib + "/Developer/CoreSimulator/Caches")]),
            Item(id: "archives", title: "Xcode archives", detail: "Archived app builds. Keep any you still need to symbolicate crash reports.",
                 symbol: "shippingbox", section: .developer, safety: .caution, targets: [.contents(lib + "/Developer/Xcode/Archives")], defaultChecked: false),
            Item(id: "npm", title: "npm / pnpm / Yarn caches", detail: "Package-manager download caches.",
                 symbol: "cube.box", section: .developer, safety: .safe,
                 targets: [.contents(h + "/.npm/_cacache"), .contents(lib + "/pnpm/store"), .contents(h + "/.yarn/berry/cache")]),
            Item(id: "gradle", title: "Gradle / Maven caches", detail: "JVM build caches (re-downloaded on next build).",
                 symbol: "cup.and.saucer", section: .developer, safety: .caution,
                 targets: [.contents(h + "/.gradle/caches"), .contents(h + "/.m2/repository")], defaultChecked: false),
            Item(id: "cargo", title: "Cargo / Go module caches", detail: "Rust registry and Go module caches.",
                 symbol: "shippingbox.circle", section: .developer, safety: .caution,
                 targets: [.contents(h + "/.cargo/registry/cache"), .contents(h + "/go/pkg/mod/cache")], defaultChecked: false),
            Item(id: "cocoapods", title: "CocoaPods / SwiftPM caches", detail: "Dependency download caches.",
                 symbol: "square.stack.3d.up", section: .developer, safety: .safe,
                 targets: [.contents(lib + "/Caches/CocoaPods"), .contents(lib + "/Caches/org.swift.swiftpm")]),

            Item(id: "browsers", title: "Browser caches", detail: "Chrome, Edge, Brave and Firefox page caches (not history, passwords or cookies).",
                 symbol: "globe", section: .apps, safety: .safe,
                 targets: [.contents(lib + "/Caches/Google/Chrome"), .contents(lib + "/Caches/Microsoft Edge"),
                           .contents(lib + "/Caches/BraveSoftware"), .contents(lib + "/Caches/Firefox")]),
            Item(id: "electron", title: "Chat & editor caches", detail: "Slack, Discord, Teams and VS Code caches.",
                 symbol: "bubble.left.and.bubble.right", section: .apps, safety: .safe,
                 targets: ["Slack", "discord", "Microsoft Teams", "Code"].flatMap { app in
                     ["Cache", "Code Cache", "GPUCache", "CachedData", "Service Worker/CacheStorage"].map { .contents(appSup + "/\(app)/\($0)") }
                 }),
            Item(id: "spotify", title: "Spotify offline cache", detail: "Streaming cache (downloaded playlists re-download).",
                 symbol: "music.note.list", section: .apps, safety: .safe, targets: [.contents(appSup + "/Spotify/PersistentCache")]),
            Item(id: "adobe", title: "Adobe media cache", detail: "Premiere/After Effects media cache files.",
                 symbol: "film.stack", section: .apps, safety: .safe,
                 targets: [.contents(appSup + "/Adobe/Common/Media Cache Files"), .contents(appSup + "/Adobe/Common/Media Cache")]),
            Item(id: "mailDownloads", title: "Mail downloads", detail: "Attachments Mail saved when you opened them (originals stay in the messages).",
                 symbol: "envelope.badge", section: .apps, safety: .safe,
                 targets: [.contents(lib + "/Containers/com.apple.mail/Data/Library/Mail Downloads")]),
            Item(id: "iosUpdates", title: "Old iPhone/iPad software updates", detail: "Firmware files Finder/iTunes downloaded.",
                 symbol: "arrow.triangle.2.circlepath", section: .apps, safety: .safe,
                 targets: [.contents(lib + "/iTunes/iPhone Software Updates"), .contents(lib + "/iTunes/iPad Software Updates")]),

            Item(id: "installers", title: "Installers in Downloads", detail: ".dmg, .pkg, .iso and .xip files you've probably already installed.",
                 symbol: "externaldrive.badge.plus", section: .downloads, safety: .safe,
                 targets: [.matching(h + "/Downloads", ["dmg", "pkg", "mpkg", "iso", "xip"])]),
            Item(id: "oldDownloads", title: "Downloads older than 6 months", detail: "Everything in Downloads that hasn't changed in 180 days.",
                 symbol: "calendar.badge.clock", section: .downloads, safety: .caution,
                 targets: [.olderThan(h + "/Downloads", days: 180)], defaultChecked: false),

            Item(id: "iosBackups", title: "iPhone / iPad backups", detail: "Local device backups. These may be your only copy — make sure you have iCloud or another backup first.",
                 symbol: "iphone.gen3.radiowaves.left.and.right", section: .personal, safety: .danger,
                 targets: [.contents(appSup + "/MobileSync/Backup")], defaultChecked: false),
            Item(id: "messages", title: "Messages attachments", detail: "Photos/videos from Messages. Deleting removes them from conversations on this Mac.",
                 symbol: "message", section: .personal, safety: .danger, targets: [.contents(lib + "/Messages/Attachments")], defaultChecked: false),
            Item(id: "docker", title: "Docker disk image", detail: "Docker's VM disk. Prefer `docker system prune` — deleting this removes all containers and images.",
                 symbol: "shippingbox.fill", section: .personal, safety: .danger,
                 targets: [.contents(lib + "/Containers/com.docker.docker/Data/vms")], defaultChecked: false),
        ]
    }
}

import AppKit
import Observation
import SwiftUI

enum MainTab: String, CaseIterable, Identifiable {
    case overview, tree, treemap, files, types, cleanup, duplicates
    var id: String { rawValue }
    var title: String {
        switch self {
        case .overview: "Overview"
        case .tree: "Tree"
        case .treemap: "Treemap"
        case .files: "Files"
        case .types: "Types"
        case .cleanup: "Cleanup"
        case .duplicates: "Duplicates"
        }
    }
    var symbol: String {
        switch self {
        case .overview: "gauge.with.dots.needle.33percent"
        case .tree: "list.bullet.indent"
        case .treemap: "square.grid.3x3.square"
        case .files: "doc.on.doc"
        case .types: "chart.pie"
        case .cleanup: "sparkles"
        case .duplicates: "square.on.square"
        }
    }
}

struct VolumeInfo: Identifiable, Hashable {
    let path: String
    let name: String
    let total: Int64
    let available: Int64
    let isInternal: Bool
    let isRemovable: Bool
    var id: String { path }
    var used: Int64 { max(0, total - available) }
    var fraction: Double { total > 0 ? Double(used) / Double(total) : 0 }

    static func load() -> [VolumeInfo] {
        let keys: [URLResourceKey] = [.volumeNameKey, .volumeTotalCapacityKey, .volumeAvailableCapacityKey,
                                      .volumeAvailableCapacityForImportantUsageKey, .volumeIsInternalKey,
                                      .volumeIsRemovableKey, .volumeIsBrowsableKey]
        let urls = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: keys, options: [.skipHiddenVolumes]) ?? []
        return urls.compactMap { url in
            guard let v = try? url.resourceValues(forKeys: Set(keys)), v.volumeIsBrowsable != false else { return nil }
            let total = Int64(v.volumeTotalCapacity ?? 0)
            guard total > 0 else { return nil }
            let important = v.volumeAvailableCapacityForImportantUsage ?? 0
            let avail = important > 0 ? important : Int64(v.volumeAvailableCapacity ?? 0)
            return VolumeInfo(path: url.path, name: v.volumeName ?? url.lastPathComponent, total: total, available: avail,
                              isInternal: v.volumeIsInternal ?? false, isRemovable: v.volumeIsRemovable ?? false)
        }
    }
}

struct DeletionItem: Identifiable {
    let id = UUID()
    let path: String
    let size: Int64
    let isDirectory: Bool
    let verdict: SafetyVerdict
}

struct DeletionRequest: Identifiable {
    let id = UUID()
    var items: [DeletionItem]
    var permanent: Bool
    var source: String
    var lockMode = false
    var allowed: [DeletionItem] { items.filter { $0.verdict.level != .protected } }
    var blocked: [DeletionItem] { items.filter { $0.verdict.level == .protected } }
    var needsTypedConfirmation: Bool { allowed.contains { $0.verdict.level == .danger } }
    var totalAllowed: Int64 { allowed.reduce(0) { $0 + $1.size } }
}

struct DeletionResult {
    var removed: [(path: String, size: Int64)] = []
    var failed: [(path: String, reason: String)] = []
    var freed: Int64 { removed.reduce(0) { $0 + $1.size } }
}

struct FilesFilter: Equatable {
    var text = ""
    var category: FileCategory?
    var ext: String?
    var minSize: Int64 = 0
    var olderThanDays = 0
    var limit = 2000
}

@MainActor
@Observable
final class AppState {
    // Launch / privileges
    var needsElevation = false
    var fullDiskAccess = FullDiskAccess.granted

    // Scan
    var root: FileNode?
    var scanPath: String?
    var isScanning = false
    var scanWasCancelled = false
    var progress = ScanProgress.Snapshot()
    var scanStarted = Date()
    var scanDuration: Double = 0
    @ObservationIgnored private var scanner: DiskScanner?
    var derived = DerivedStats()
    var isRebuilding = false

    // UI
    var tab: MainTab = .tree
    var selection: [FileNode] = []
    var showInspector = true
    var treeVersion = 0
    var sortStamp = 0
    var treemapRoot: FileNode?
    var filesFilter = FilesFilter()
    var volumes: [VolumeInfo] = VolumeInfo.load()
    var deletionRequest: DeletionRequest?
    var lastResult: DeletionResult?
    var errorMessage: String?
    @ObservationIgnored var revealHandler: ((FileNode) -> Void)?

    let cleanup = CleanupModel()
    let duplicates = DuplicatesModel()

    // Settings
    var sizeMode: SizeMode = SizeMode(rawValue: UserDefaults.standard.string(forKey: "sizeMode") ?? "") ?? .allocated {
        didSet {
            UserDefaults.standard.set(sizeMode.rawValue, forKey: "sizeMode")
            sortStamp += 1
            rebuildDerived()
        }
    }
    var stayOnVolume: Bool = UserDefaults.standard.object(forKey: "stayOnVolume") as? Bool ?? true {
        didSet { UserDefaults.standard.set(stayOnVolume, forKey: "stayOnVolume") }
    }
    var excludedPaths: [String] = UserDefaults.standard.stringArray(forKey: "excludedPaths") ?? [] {
        didSet { UserDefaults.standard.set(excludedPaths, forKey: "excludedPaths") }
    }
    var recentPaths: [String] = UserDefaults.standard.stringArray(forKey: "recentPaths") ?? [] {
        didSet { UserDefaults.standard.set(recentPaths, forKey: "recentPaths") }
    }
    var defaultPermanent: Bool = UserDefaults.standard.bool(forKey: "defaultPermanent") {
        didSet { UserDefaults.standard.set(defaultPermanent, forKey: "defaultPermanent") }
    }

    var focused: FileNode? { selection.first ?? root }

    var rootVolume: VolumeInfo? {
        guard let p = scanPath else { return nil }
        let norm = SafetyClassifier.normalize(p)
        return volumes
            .filter { v in v.path == "/" ? !norm.hasPrefix("/Volumes/") : (norm == v.path || norm.hasPrefix(v.path + "/")) }
            .max { $0.path.count < $1.path.count }
    }

    // MARK: - Scanning

    func scan(_ path: String) {
        cancelScan()
        let s = DiskScanner(options: .init(stayOnVolume: stayOnVolume, excludedPaths: excludedPaths))
        scanner = s
        isScanning = true
        scanWasCancelled = false
        scanPath = path
        scanStarted = Date()
        progress = .init()
        selection = []
        treemapRoot = nil
        duplicates.reset()
        recentPaths = [path] + recentPaths.filter { $0 != path }.prefix(7)
        let mode = sizeMode

        Task { [weak self] in
            while let self, self.isScanning, self.scanner === s {
                self.progress = s.progress.snapshot()
                try? await Task.sleep(for: .milliseconds(120))
            }
        }
        Task.detached(priority: .userInitiated) { [weak self] in
            let root = s.scan(rootPath: path)
            let derived = root.map { DerivedStats.build(root: $0, mode: mode) } ?? DerivedStats()
            await MainActor.run {
                guard let self, self.scanner === s else { return }
                self.progress = s.progress.snapshot()
                self.scanDuration = Date().timeIntervalSince(self.scanStarted)
                self.scanWasCancelled = s.isCancelled
                self.root = root
                self.derived = derived
                self.isScanning = false
                self.scanner = nil
                self.sortStamp += 1
                self.treeVersion += 1
                self.volumes = VolumeInfo.load()
                self.fullDiskAccess = FullDiskAccess.granted
                if root == nil { self.errorMessage = "Could not read \(path)." }
            }
        }
    }

    func cancelScan() {
        scanner?.cancel()
    }

    func rescan() {
        if let p = scanPath { scan(p) }
    }

    /// Re-measure one folder and graft the result into the existing tree.
    func rescan(_ node: FileNode) {
        guard node.isDirectory else { return }
        guard let parent = node.parent else { rescan(); return }
        let path = node.path
        let s = DiskScanner(options: .init(stayOnVolume: stayOnVolume, excludedPaths: excludedPaths))
        Task.detached(priority: .userInitiated) { [weak self] in
            guard let fresh = s.scan(rootPath: path) else { return }
            await MainActor.run {
                guard let self else { return }
                parent.replaceChild(node, with: fresh)
                self.selection = [fresh]
                self.afterTreeChange()
            }
        }
    }

    func chooseFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.prompt = "Scan"
        panel.directoryURL = URL(fileURLWithPath: RealUser.home)
        if panel.runModal() == .OK, let url = panel.url { scan(url.path) }
    }

    func afterTreeChange() {
        sortStamp += 1
        treeVersion += 1
        volumes = VolumeInfo.load()
        rebuildDerived()
    }

    func rebuildDerived() {
        guard let root else { return }
        let mode = sizeMode
        isRebuilding = true
        Task.detached(priority: .userInitiated) { [weak self] in
            let d = DerivedStats.build(root: root, mode: mode)
            await MainActor.run {
                guard let self, self.root === root else { return }
                self.derived = d
                self.isRebuilding = false
            }
        }
    }

    // MARK: - Selection & navigation

    func select(_ node: FileNode, reveal: Bool = true) {
        selection = [node]
        if reveal { revealHandler?(node) }
    }

    func showInTree(_ node: FileNode) {
        tab = .tree
        selection = [node]
        DispatchQueue.main.async { self.revealHandler?(node) }
    }

    func showInTreemap(_ node: FileNode) {
        treemapRoot = node.isDirectory ? node : node.parent
        selection = [node]
        tab = .treemap
    }

    func excludeFromScans(_ path: String) {
        if !excludedPaths.contains(path) { excludedPaths.append(path) }
    }

    // MARK: - Deletion

    func requestDelete(_ nodes: [FileNode], permanent: Bool? = nil, source: String = "Selection") {
        let set = Set(nodes)
        // Drop anything already covered by a selected ancestor.
        let top = nodes.filter { n in !n.ancestors.contains { set.contains($0) } }
        let items = top.map { n in
            DeletionItem(path: n.path, size: n.size(sizeMode), isDirectory: n.isDirectory, verdict: SafetyClassifier.classify(node: n))
        }
        guard !items.isEmpty else { return }
        lastResult = nil
        deletionRequest = DeletionRequest(items: items, permanent: permanent ?? defaultPermanent, source: source)
    }

    func requestDelete(paths: [(path: String, size: Int64)], permanent: Bool? = nil, lockMode: Bool = false, source: String) {
        let sorted = paths.sorted { $0.path.count < $1.path.count }
        var kept: [(String, Int64)] = []
        for p in sorted where !kept.contains(where: { p.path == $0.0 || p.path.hasPrefix($0.0 + "/") }) {
            kept.append((p.path, p.size))
        }
        let items = kept.map { p, s in
            var st = stat()
            let isDir = lstat(p, &st) == 0 && (st.st_mode & S_IFMT) == S_IFDIR
            return DeletionItem(path: p, size: s, isDirectory: isDir, verdict: SafetyClassifier.classify(path: p))
        }
        guard !items.isEmpty else { return }
        lastResult = nil
        deletionRequest = DeletionRequest(items: items, permanent: permanent ?? defaultPermanent, source: source, lockMode: lockMode)
    }

    func perform(_ request: DeletionRequest) async -> DeletionResult {
        let allowed = request.allowed
        let permanent = request.permanent
        let result = await Task.detached(priority: .userInitiated) { () -> DeletionResult in
            var r = DeletionResult()
            for item in allowed {
                // Re-check at the last moment in case something changed since the sheet opened.
                let verdict = SafetyClassifier.classify(path: item.path)
                if verdict.level == .protected {
                    r.failed.append((item.path, "Blocked: " + (verdict.reasons.first ?? "protected")))
                    continue
                }
                do {
                    if permanent {
                        try FileOps.deletePermanently(item.path)
                        FileOps.log("DELETE", path: item.path, size: item.size)
                    } else {
                        let dest = try FileOps.moveToTrash(item.path)
                        FileOps.log("TRASH", path: item.path, size: item.size, detail: "-> " + dest)
                    }
                    r.removed.append((item.path, item.size))
                } catch {
                    r.failed.append((item.path, error.localizedDescription))
                    FileOps.log("FAILED", path: item.path, size: item.size, detail: error.localizedDescription)
                }
            }
            return r
        }.value

        var touched = false
        if let root {
            for (path, _) in result.removed {
                if let node = root.find(path: path), node !== root {
                    selection.removeAll { $0 === node || $0.isDescendant(of: node) }
                    if let tm = treemapRoot, tm === node || tm.isDescendant(of: node) { treemapRoot = node.parent }
                    node.detach()
                    touched = true
                }
            }
            // Partially-deleted folders: re-measure them so the numbers stay honest.
            for (path, _) in result.failed {
                if let node = root.find(path: path), node.isDirectory, FileManager.default.fileExists(atPath: path) {
                    rescan(node)
                }
            }
        }
        duplicates.removeDeleted(paths: Set(result.removed.map(\.path)))
        if touched { afterTreeChange() } else { volumes = VolumeInfo.load() }
        cleanup.refreshSizes()
        lastResult = result
        return result
    }

    // MARK: - Export

    func exportCSV() {
        guard let root else { return }
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "DiskWatch Report.csv"
        panel.directoryURL = URL(fileURLWithPath: RealUser.home + "/Desktop")
        guard panel.runModal() == .OK, let url = panel.url else { return }
        var out = "Path,Type,Size on Disk (bytes),File Size (bytes),Files,Folders,Modified,Owner\n"
        func esc(_ s: String) -> String { "\"" + s.replacingOccurrences(of: "\"", with: "\"\"") + "\"" }
        var stack: [FileNode] = [root]
        while let n = stack.popLast() {
            if n.isDirectory || n.allocatedSize >= 10_000_000 {
                out += [esc(n.path), n.isDirectory ? "Folder" : "File", "\(n.allocatedSize)", "\(n.logicalSize)",
                        "\(n.isDirectory ? n.fileCount : 1)", "\(n.dirCount)",
                        ISO8601DateFormatter().string(from: Date(timeIntervalSince1970: n.modified)), Fmt.user(n.uid)]
                    .joined(separator: ",") + "\n"
            }
            if n.isDirectory { stack.append(contentsOf: n.children.reversed()) }
        }
        do { try FileOps.writeUserFile(Data(out.utf8), to: url.path) } catch { errorMessage = error.localizedDescription }
    }
}

import AppKit
import CryptoKit
import Foundation
import Observation

@MainActor
@Observable
final class DuplicatesModel {
    struct Group: Identifiable {
        let id = UUID()
        let size: Int64
        var files: [FileNode]
        var wasted: Int64 { size * Int64(max(0, files.count - 1)) }
    }

    var groups: [Group] = []
    var isRunning = false
    var progress: Double = 0
    var status = ""
    var marked: Set<ObjectIdentifier> = []
    var minSize: Int64 = 1_000_000
    var hasRun = false
    @ObservationIgnored private var cancelFlag = CancelFlag()

    final class CancelFlag: @unchecked Sendable {
        private let lock = NSLock()
        private var v = false
        var value: Bool { lock.lock(); defer { lock.unlock() }; return v }
        func set() { lock.lock(); v = true; lock.unlock() }
    }

    var totalWasted: Int64 { groups.reduce(0) { $0 + $1.wasted } }
    var markedSize: Int64 { groups.flatMap(\.files).filter { marked.contains($0.id) }.reduce(0) { $0 + $1.allocatedSize } }
    var markedNodes: [FileNode] { groups.flatMap(\.files).filter { marked.contains($0.id) } }

    func reset() {
        cancel()
        groups = []
        marked = []
        hasRun = false
        status = ""
    }

    func cancel() { cancelFlag.set() }

    func run(files: [FileNode]) {
        cancel()
        let flag = CancelFlag()
        cancelFlag = flag
        isRunning = true
        hasRun = true
        progress = 0
        groups = []
        marked = []
        status = "Grouping by size…"
        let min = minSize
        // Resolve paths up front on the main actor; the tree may change while hashing.
        let candidates: [(FileNode, String)] = {
            var bySize: [Int64: [FileNode]] = [:]
            for f in files where f.logicalSize >= min && !f.isSymlink && !f.hardlinkDuplicate {
                bySize[f.logicalSize, default: []].append(f)
            }
            return bySize.values.filter { $0.count > 1 }.flatMap { $0 }.map { ($0, $0.path) }
        }()

        Task.detached(priority: .utility) { [weak self] in
            let found = DuplicatesModel.find(candidates, flag: flag) { p, s in
                Task { @MainActor in self?.progress = p; self?.status = s }
            }
            await MainActor.run {
                guard let self, !flag.value else { return }
                self.groups = found.sorted { $0.wasted > $1.wasted }
                self.isRunning = false
                self.status = found.isEmpty ? "No duplicates found." : "\(found.count) groups of identical files."
            }
        }
    }

    func stop() {
        cancel()
        isRunning = false
        status = "Stopped."
    }

    enum Keep { case newest, oldest, shortestPath }

    func autoMark(keep: Keep) {
        marked = []
        for g in groups {
            let keeper: FileNode?
            switch keep {
            case .newest: keeper = g.files.max { $0.modified < $1.modified }
            case .oldest: keeper = g.files.min { $0.modified < $1.modified }
            case .shortestPath: keeper = g.files.min { $0.path.count < $1.path.count }
            }
            for f in g.files where f !== keeper { marked.insert(f.id) }
        }
    }

    /// Marks/unmarks a copy, but never lets every copy in a group be marked (that would delete the data outright).
    func toggle(_ f: FileNode) {
        if marked.contains(f.id) { marked.remove(f.id); return }
        if let g = groups.first(where: { $0.files.contains { $0 === f } }),
           g.files.filter({ $0 !== f && !marked.contains($0.id) }).isEmpty {
            status = "Keep at least one copy of \(f.name) — DiskWatch won't mark every copy."
            NSSound.beep()
            return
        }
        marked.insert(f.id)
    }

    func removeDeleted(paths: Set<String>) {
        guard !groups.isEmpty else { return }
        groups = groups.compactMap { g in
            var g = g
            g.files.removeAll { paths.contains($0.path) || $0.parent == nil }
            return g.files.count > 1 ? g : nil
        }
        let alive = Set(groups.flatMap(\.files).map(\.id))
        marked = marked.intersection(alive)
    }

    nonisolated static func find(_ candidates: [(FileNode, String)], flag: CancelFlag,
                                 progress: @escaping (Double, String) -> Void) -> [Group] {
        var bySize: [Int64: [(FileNode, String)]] = [:]
        for c in candidates { bySize[c.0.logicalSize, default: []].append(c) }
        let sizeGroups = Array(bySize.values)
        var result: [Group] = []
        let totalBytes = Double(candidates.reduce(Int64(0)) { $0 + $1.0.logicalSize })
        var doneBytes = 0.0
        var lastReport = Date.distantPast

        for group in sizeGroups {
            if flag.value { return [] }
            // Skip hard links to the same inode.
            var seenInodes = Set<UInt64>()
            let unique = group.filter { seenInodes.insert($0.0.inode).inserted }
            guard unique.count > 1 else { continue }
            // Stage 1: head + tail sample.
            var byPartial: [Data: [(FileNode, String)]] = [:]
            for item in unique {
                if let h = hash(item.1, full: false) { byPartial[h, default: []].append(item) }
            }
            // Stage 2: full content hash for anything still colliding.
            for (_, same) in byPartial where same.count > 1 {
                var byFull: [Data: [FileNode]] = [:]
                for item in same {
                    if flag.value { return [] }
                    if let h = hash(item.1, full: true) { byFull[h, default: []].append(item.0) }
                    doneBytes += Double(item.0.logicalSize)
                    if Date().timeIntervalSince(lastReport) > 0.15 {
                        lastReport = Date()
                        progress(min(1, doneBytes / max(1, totalBytes)), "Comparing \((item.1 as NSString).lastPathComponent)")
                    }
                }
                for (_, files) in byFull where files.count > 1 {
                    result.append(Group(size: files[0].allocatedSize, files: files))
                }
            }
        }
        progress(1, "Done")
        return result
    }

    nonisolated static func hash(_ path: String, full: Bool) -> Data? {
        guard let h = FileHandle(forReadingAtPath: path) else { return nil }
        defer { try? h.close() }
        var hasher = SHA256()
        let chunk = 1 << 20
        if full {
            while true {
                guard let d = try? h.read(upToCount: chunk), !d.isEmpty else { break }
                hasher.update(data: d)
            }
        } else {
            if let d = try? h.read(upToCount: 64 * 1024) { hasher.update(data: d) }
            if let end = try? h.seekToEnd(), end > 128 * 1024 {
                try? h.seek(toOffset: end - 64 * 1024)
                if let d = try? h.read(upToCount: 64 * 1024) { hasher.update(data: d) }
            }
        }
        return Data(hasher.finalize())
    }
}

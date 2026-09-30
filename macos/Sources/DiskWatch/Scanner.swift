import Foundation

struct MountInfo {
    let path: String
    let from: String
    let fsType: String

    /// APFS container ("disk3") for a device like "/dev/disk3s1s1".
    var container: String? {
        guard from.hasPrefix("/dev/disk") else { return nil }
        let rest = from.dropFirst("/dev/".count)
        var out = "disk"
        for ch in rest.dropFirst(4) {
            if ch.isNumber { out.append(ch) } else { break }
        }
        return out == "disk" ? nil : out
    }

    static func all() -> [MountInfo] {
        var buf: UnsafeMutablePointer<statfs>?
        let n = getmntinfo(&buf, MNT_NOWAIT)
        guard n > 0, let buf else { return [] }
        return (0..<Int(n)).map { i in
            var s = buf[i]
            return MountInfo(
                path: cString(&s.f_mntonname),
                from: cString(&s.f_mntfromname),
                fsType: cString(&s.f_fstypename))
        }
    }

    static func containing(_ path: String) -> MountInfo? {
        var s = statfs()
        guard statfs(path, &s) == 0 else { return nil }
        return MountInfo(path: cString(&s.f_mntonname), from: cString(&s.f_mntfromname), fsType: cString(&s.f_fstypename))
    }
}

func cString<T>(_ tuple: inout T) -> String {
    withUnsafePointer(to: &tuple) {
        $0.withMemoryRebound(to: CChar.self, capacity: MemoryLayout<T>.size) { String(cString: $0) }
    }
}

final class ScanProgress: @unchecked Sendable {
    private let lock = NSLock()
    private var _files = 0, _dirs = 0, _errors = 0
    private var _bytes: Int64 = 0
    private var _path = ""

    func add(files: Int, dirs: Int, bytes: Int64, errors: Int, path: String) {
        lock.lock()
        _files += files; _dirs += dirs; _bytes += bytes; _errors += errors
        _path = path
        lock.unlock()
    }

    struct Snapshot { var files = 0, dirs = 0, errors = 0; var bytes: Int64 = 0; var path = "" }

    func snapshot() -> Snapshot {
        lock.lock(); defer { lock.unlock() }
        return Snapshot(files: _files, dirs: _dirs, errors: _errors, bytes: _bytes, path: _path)
    }
}

/// Multi-threaded directory walker built on openat/readdir/fstatat.
final class DiskScanner: @unchecked Sendable {
    struct Options {
        var stayOnVolume = true
        var excludedPaths: [String] = []
    }

    let progress = ScanProgress()
    private let options: Options
    private let cond = NSCondition()
    private var stack: [(FileNode, String)] = []
    private var pending = 0
    private var cancelled = false
    private var skipPaths: Set<String> = []
    private let linkLock = NSLock()
    private var seenLinks: Set<UInt64> = []

    init(options: Options = Options()) {
        self.options = options
    }

    var isCancelled: Bool { cond.lock(); defer { cond.unlock() }; return cancelled }

    func cancel() {
        cond.lock()
        cancelled = true
        cond.broadcast()
        cond.unlock()
    }

    private func computeSkips(root: String) {
        var skips = Set(options.excludedPaths)
        let rootMount = MountInfo.containing(root)
        let rootContainer = rootMount?.container
        for m in MountInfo.all() where m.path != root && m.path != "/" {
            if ["autofs", "devfs", "nullfs"].contains(m.fsType) { skips.insert(m.path); continue }
            // The Data volume is already visible at / through firmlinks; walking it again would double count.
            if m.path == "/System/Volumes/Data" && !root.hasPrefix("/System/Volumes/Data") { skips.insert(m.path); continue }
            if options.stayOnVolume {
                let same = rootContainer != nil && m.container == rootContainer
                if !same { skips.insert(m.path) }
            }
        }
        skips.remove(root)
        skipPaths = skips
    }

    /// Scans `rootPath` and returns the aggregated tree. Blocks the calling thread.
    func scan(rootPath: String) -> FileNode? {
        var st = stat()
        guard lstat(rootPath, &st) == 0 else { return nil }
        let root = FileNode(name: rootPath, parent: nil, st: st)
        guard root.isDirectory else { return root }
        computeSkips(root: rootPath)
        stack = [(root, rootPath)]
        pending = 1
        let workers = Int(ProcessInfo.processInfo.environment["DW_WORKERS"] ?? "") ?? max(2, min(8, ProcessInfo.processInfo.activeProcessorCount))
        DispatchQueue.concurrentPerform(iterations: workers) { _ in worker() }
        DiskScanner.aggregate(root)
        return root
    }

    private func worker() {
        while true {
            cond.lock()
            while stack.isEmpty && pending > 0 && !cancelled { cond.wait() }
            if stack.isEmpty || cancelled {
                cond.broadcast()
                cond.unlock()
                return
            }
            let (node, path) = stack.removeLast()
            cond.unlock()

            let subdirs = process(node: node, path: path)

            cond.lock()
            stack.append(contentsOf: subdirs)
            pending += subdirs.count - 1
            if pending == 0 || !subdirs.isEmpty { cond.broadcast() }
            cond.unlock()
        }
    }

    private func process(node: FileNode, path: String) -> [(FileNode, String)] {
        let fd = open(path, O_RDONLY | O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC)
        if fd < 0 {
            node.unreadable = true
            progress.add(files: 0, dirs: 0, bytes: 0, errors: 1, path: path)
            return []
        }
        guard let dir = fdopendir(fd) else {
            close(fd)
            node.unreadable = true
            progress.add(files: 0, dirs: 0, bytes: 0, errors: 1, path: path)
            return []
        }
        defer { closedir(dir) }

        var kids: [FileNode] = []
        var subdirs: [(FileNode, String)] = []
        var files = 0, dirs = 0, errors = 0
        var bytes: Int64 = 0
        let base = path == "/" ? "/" : path + "/"

        while let ent = readdir(dir) {
            var st = stat()
            var name = ""
            let ok: Bool = withUnsafePointer(to: &ent.pointee.d_name) { tp in
                tp.withMemoryRebound(to: CChar.self, capacity: 1024) { p in
                    if p[0] == 46 && (p[1] == 0 || (p[1] == 46 && p[2] == 0)) { return false } // "." / ".."
                    name = String(cString: p)
                    if fstatat(fd, p, &st, AT_SYMLINK_NOFOLLOW) != 0 { errors += 1; return false }
                    return true
                }
            }
            if !ok { continue }
            let child = FileNode(name: name, parent: node, st: st)
            if child.isDirectory {
                let childPath = base + name
                if skipPaths.contains(childPath) {
                    child.excluded = true
                } else {
                    subdirs.append((child, childPath))
                }
                dirs += 1
            } else {
                if st.st_nlink > 1 {
                    let key = (UInt64(UInt32(bitPattern: st.st_dev)) << 40) ^ st.st_ino
                    linkLock.lock()
                    let fresh = seenLinks.insert(key).inserted
                    linkLock.unlock()
                    if !fresh {
                        child.hardlinkDuplicate = true
                        child.allocatedSize = 0
                        child.logicalSize = 0
                    }
                }
                files += 1
                bytes += child.allocatedSize
            }
            kids.append(child)
        }
        node.children = kids
        progress.add(files: files, dirs: dirs, bytes: bytes, errors: errors, path: path)
        return subdirs
    }

    /// Roll child totals up into every directory, sort children by size, and mark app-bundle contents.
    static func aggregate(_ root: FileNode) {
        var order: [FileNode] = [root]
        var i = 0
        while i < order.count {
            let n = order[i]; i += 1
            if n.isDirectory {
                let inApp = n.inAppBundle || (n.isDirectory && ["app", "framework", "appex"].contains(n.nameExtension))
                for c in n.children {
                    c.inAppBundle = inApp
                    order.append(c)
                }
            }
        }
        for n in order.reversed() where n.isDirectory {
            var l: Int64 = 0, a: Int64 = 0
            var f = 0, d = 0
            for c in n.children {
                l += c.logicalSize
                a += c.allocatedSize
                if c.isDirectory { d += 1 + c.dirCount; f += c.fileCount } else { f += 1 }
            }
            n.logicalSize = l
            n.allocatedSize = a
            n.fileCount = f
            n.dirCount = d
            n.children.sort { $0.allocatedSize > $1.allocatedSize }
        }
    }

    /// Quick total for a single path (used by cleanup suggestions).
    static func measure(_ path: String) -> (allocated: Int64, logical: Int64, files: Int) {
        let s = DiskScanner(options: .init(stayOnVolume: true))
        guard let n = s.scan(rootPath: path) else { return (0, 0, 0) }
        return (n.allocatedSize, n.logicalSize, n.isDirectory ? n.fileCount : 1)
    }
}

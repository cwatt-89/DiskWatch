import Foundation

enum SizeMode: String, CaseIterable, Identifiable {
    case allocated, logical
    var id: String { rawValue }
    var title: String { self == .allocated ? "Size on Disk" : "File Size" }
    var other: SizeMode { self == .allocated ? .logical : .allocated }
}

/// One file or folder in a scan. Directories aggregate the sizes of everything below them.
final class FileNode: Identifiable, Hashable {
    var name: String
    weak var parent: FileNode?
    let isDirectory: Bool
    let isSymlink: Bool
    var logicalSize: Int64
    var allocatedSize: Int64
    var fileCount = 0
    var dirCount = 0
    let modified: Double
    let accessed: Double
    let created: Double
    let uid: UInt32
    let gid: UInt32
    let mode: UInt16
    let flags: UInt32
    let inode: UInt64
    let device: Int32
    let linkCount: UInt16
    var children: [FileNode] = []

    var unreadable = false
    var excluded = false
    var hardlinkDuplicate = false
    var inAppBundle = false
    var sortStamp = -1

    init(name: String, parent: FileNode?, st: stat) {
        self.name = name
        self.parent = parent
        let fmt = st.st_mode & S_IFMT
        isDirectory = fmt == S_IFDIR
        isSymlink = fmt == S_IFLNK
        logicalSize = isDirectory ? 0 : Int64(st.st_size)
        allocatedSize = isDirectory ? 0 : Int64(st.st_blocks) * 512
        modified = Double(st.st_mtimespec.tv_sec) + Double(st.st_mtimespec.tv_nsec) / 1e9
        accessed = Double(st.st_atimespec.tv_sec)
        created = Double(st.st_birthtimespec.tv_sec)
        uid = st.st_uid
        gid = st.st_gid
        mode = st.st_mode
        flags = st.st_flags
        inode = st.st_ino
        device = st.st_dev
        linkCount = st.st_nlink
    }

    var id: ObjectIdentifier { ObjectIdentifier(self) }
    static func == (a: FileNode, b: FileNode) -> Bool { a === b }
    func hash(into h: inout Hasher) { h.combine(ObjectIdentifier(self)) }

    func size(_ mode: SizeMode) -> Int64 { mode == .allocated ? allocatedSize : logicalSize }

    var path: String {
        var parts: [String] = []
        var n: FileNode? = self
        while let c = n { parts.append(c.name); n = c.parent }
        var result = parts.removeLast()
        for p in parts.reversed() {
            if !result.hasSuffix("/") { result += "/" }
            result += p
        }
        return result
    }

    var displayName: String {
        parent == nil && name == "/" ? "Macintosh HD  /" : name
    }

    /// Lowercased extension of the name (for files and bundles alike).
    var nameExtension: String {
        guard let dot = name.lastIndex(of: "."), dot != name.startIndex else { return "" }
        return name[name.index(after: dot)...].lowercased()
    }

    var fileExtension: String { isDirectory ? "" : nameExtension }

    var isPackage: Bool {
        isDirectory && FileNode.packageExtensions.contains(nameExtension)
    }

    static let packageExtensions: Set<String> = [
        "app", "framework", "bundle", "plugin", "kext", "photoslibrary", "musiclibrary",
        "tvlibrary", "xcodeproj", "xcworkspace", "playground", "pkg", "mpkg", "rtfd",
        "sparsebundle", "utm", "pvm", "vmwarevm", "fcpbundle", "logicx", "band", "imovielibrary",
        "xcarchive", "appex", "prefpane", "saver", "qlgenerator", "mdimporter", "docarchive", "lproj"
    ]

    var itemCount: Int { fileCount + dirCount }

    var depth: Int {
        var d = 0
        var n = parent
        while let p = n { d += 1; n = p.parent }
        return d
    }

    var ancestors: [FileNode] {
        var out: [FileNode] = []
        var n = parent
        while let p = n { out.append(p); n = p.parent }
        return out
    }

    func isDescendant(of other: FileNode) -> Bool {
        var n = parent
        while let p = n {
            if p === other { return true }
            n = p.parent
        }
        return false
    }

    func child(named n: String) -> FileNode? { children.first { $0.name == n } }

    func fraction(ofParent mode: SizeMode) -> Double {
        guard let p = parent else { return 1 }
        let total = p.size(mode)
        return total > 0 ? Double(size(mode)) / Double(total) : 0
    }

    /// Remove this node from its parent and subtract its totals from every ancestor.
    func detach() {
        guard let p = parent else { return }
        p.children.removeAll { $0 === self }
        let files = isDirectory ? fileCount : 1
        let dirs = isDirectory ? dirCount + 1 : 0
        var n: FileNode? = p
        while let a = n {
            a.logicalSize -= logicalSize
            a.allocatedSize -= allocatedSize
            a.fileCount -= files
            a.dirCount -= dirs
            n = a.parent
        }
        parent = nil
    }

    /// Replace `old` (a child of this node) with a freshly scanned `new` node, fixing totals up the chain.
    func replaceChild(_ old: FileNode, with new: FileNode) {
        guard let idx = children.firstIndex(where: { $0 === old }) else { return }
        new.name = old.name
        new.parent = self
        new.inAppBundle = old.inAppBundle
        children[idx] = new
        let dl = new.logicalSize - old.logicalSize
        let da = new.allocatedSize - old.allocatedSize
        let df = (new.isDirectory ? new.fileCount : 1) - (old.isDirectory ? old.fileCount : 1)
        let dd = (new.isDirectory ? new.dirCount + 1 : 0) - (old.isDirectory ? old.dirCount + 1 : 0)
        var n: FileNode? = self
        while let a = n {
            a.logicalSize += dl
            a.allocatedSize += da
            a.fileCount += df
            a.dirCount += dd
            n = a.parent
        }
        old.parent = nil
    }

    /// Find a node by absolute path under this root.
    func find(path target: String) -> FileNode? {
        let rootPath = path
        if target == rootPath { return self }
        let prefix = rootPath.hasSuffix("/") ? rootPath : rootPath + "/"
        guard target.hasPrefix(prefix) else { return nil }
        let rest = target.dropFirst(prefix.count).split(separator: "/")
        var node: FileNode = self
        for comp in rest {
            guard let next = node.child(named: String(comp)) else { return nil }
            node = next
        }
        return node
    }
}

import AppKit
import Foundation

enum Fmt {
    private static let byteFormatter: ByteCountFormatter = {
        let f = ByteCountFormatter()
        f.countStyle = .file
        f.allowsNonnumericFormatting = false
        return f
    }()

    private static let dateFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateStyle = .medium
        f.timeStyle = .short
        return f
    }()

    private static let shortDate: DateFormatter = {
        let f = DateFormatter()
        f.dateStyle = .short
        f.timeStyle = .none
        return f
    }()

    private static let numberFormatter: NumberFormatter = {
        let f = NumberFormatter()
        f.numberStyle = .decimal
        return f
    }()

    private static let relativeFormatter: RelativeDateTimeFormatter = {
        let f = RelativeDateTimeFormatter()
        f.unitsStyle = .full
        return f
    }()

    static func bytes(_ v: Int64) -> String { byteFormatter.string(fromByteCount: v) }
    static func date(_ t: Double) -> String { t <= 0 ? "—" : dateFormatter.string(from: Date(timeIntervalSince1970: t)) }
    static func shortDate(_ t: Double) -> String { t <= 0 ? "—" : shortDate.string(from: Date(timeIntervalSince1970: t)) }
    static func count(_ n: Int) -> String { numberFormatter.string(from: NSNumber(value: n)) ?? "\(n)" }
    static func percent(_ f: Double) -> String { f >= 0.9995 ? "100%" : String(format: "%.1f%%", f * 100) }
    static func relative(_ t: Double) -> String {
        t <= 0 ? "—" : relativeFormatter.localizedString(for: Date(timeIntervalSince1970: t), relativeTo: Date())
    }

    static func duration(_ s: Double) -> String {
        if s < 60 { return String(format: "%.1f s", s) }
        return String(format: "%d min %02d s", Int(s) / 60, Int(s) % 60)
    }

    static func permissions(_ mode: UInt16) -> String {
        let type: String
        switch mode & S_IFMT {
        case S_IFDIR: type = "d"
        case S_IFLNK: type = "l"
        default: type = "-"
        }
        let bits: [(UInt16, Character)] = [(0o400, "r"), (0o200, "w"), (0o100, "x"), (0o040, "r"), (0o020, "w"),
                                           (0o010, "x"), (0o004, "r"), (0o002, "w"), (0o001, "x")]
        return type + String(bits.map { mode & $0.0 != 0 ? $0.1 : "-" })
    }

    private static var userCache: [UInt32: String] = [:]
    private static var groupCache: [UInt32: String] = [:]
    private static let cacheLock = NSLock()

    static func user(_ uid: UInt32) -> String {
        cacheLock.lock(); defer { cacheLock.unlock() }
        if let n = userCache[uid] { return n }
        let n = getpwuid(uid).map { String(cString: $0.pointee.pw_name) } ?? "\(uid)"
        userCache[uid] = n
        return n
    }

    static func group(_ gid: UInt32) -> String {
        cacheLock.lock(); defer { cacheLock.unlock() }
        if let n = groupCache[gid] { return n }
        let n = getgrgid(gid).map { String(cString: $0.pointee.gr_name) } ?? "\(gid)"
        groupCache[gid] = n
        return n
    }

    static func abbreviate(_ path: String) -> String {
        let home = RealUser.home
        if path == home { return "~" }
        if path.hasPrefix(home + "/") { return "~" + path.dropFirst(home.count) }
        return path
    }
}

enum Icons {
    private static var cache: [String: NSImage] = [:]

    static func icon(for node: FileNode) -> NSImage {
        if node.isDirectory {
            if node.isPackage || node.parent == nil { return NSWorkspace.shared.icon(forFile: node.path) }
            return cached("__folder") { NSWorkspace.shared.icon(for: .folder) }
        }
        if node.isSymlink { return cached("__link") { NSWorkspace.shared.icon(for: .symbolicLink) } }
        let ext = node.nameExtension
        return cached("ext." + ext) {
            if let t = UTType(filenameExtension: ext) { return NSWorkspace.shared.icon(for: t) }
            return NSWorkspace.shared.icon(for: .data)
        }
    }

    static func icon(forPath path: String) -> NSImage { NSWorkspace.shared.icon(forFile: path) }

    private static func cached(_ key: String, _ make: () -> NSImage) -> NSImage {
        if let i = cache[key] { return i }
        let i = make()
        cache[key] = i
        return i
    }
}

import UniformTypeIdentifiers

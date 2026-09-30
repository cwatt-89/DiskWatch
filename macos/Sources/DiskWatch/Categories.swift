import SwiftUI

enum FileCategory: String, CaseIterable, Identifiable {
    case video, audio, images, documents, archives, installers, applications, code, virtualMachines, system, other

    var id: String { rawValue }

    var title: String {
        switch self {
        case .video: "Video"
        case .audio: "Audio"
        case .images: "Images"
        case .documents: "Documents"
        case .archives: "Archives"
        case .installers: "Disk Images & Installers"
        case .applications: "Applications"
        case .code: "Code & Developer"
        case .virtualMachines: "Virtual Machines"
        case .system: "System & Libraries"
        case .other: "Other"
        }
    }

    var symbol: String {
        switch self {
        case .video: "film"
        case .audio: "music.note"
        case .images: "photo"
        case .documents: "doc.text"
        case .archives: "archivebox"
        case .installers: "externaldrive.badge.plus"
        case .applications: "app.badge"
        case .code: "chevron.left.forwardslash.chevron.right"
        case .virtualMachines: "desktopcomputer"
        case .system: "gearshape.2"
        case .other: "questionmark.square.dashed"
        }
    }

    var color: Color { Color(nsColor: nsColor) }

    /// One-accent palette: kinds differ by lightness (accent family, then greys), never by a new hue.
    var nsColor: NSColor {
        switch self {
        case .video: FN.NS.accentBright
        case .images: FN.NS.accent
        case .audio: FN.NS.accentDim
        case .documents: FN.NS.fg
        case .archives: NSColor(hex: 0xC4C4C4)
        case .installers: NSColor(hex: 0xA6A6A6)
        case .applications: FN.NS.muted
        case .code: NSColor(hex: 0x6B6B6B)
        case .virtualMachines: NSColor(hex: 0x555555)
        case .system: FN.NS.hair
        case .other: NSColor(hex: 0x333333)
        }
    }

    /// Text drawn on top of a `nsColor` fill.
    var onFill: Color {
        switch self {
        case .video, .images, .audio, .documents, .archives, .installers, .applications: FN.bg
        default: FN.fg
        }
    }

    private static let map: [String: FileCategory] = {
        var m: [String: FileCategory] = [:]
        func add(_ c: FileCategory, _ exts: String) { for e in exts.split(separator: " ") { m[String(e)] = c } }
        add(.video, "mp4 m4v mov avi mkv wmv flv webm mpg mpeg 3gp mts m2ts ts vob braw r3d mxf prores hevc")
        add(.audio, "mp3 m4a aac wav aif aiff flac ogg opus wma alac caf mid midi m4b m4p aup3 logicx band")
        add(.images, "jpg jpeg png gif heic heif tif tiff bmp webp raw cr2 cr3 nef arw dng orf rw2 psd psb ai svg ico icns xcf sketch fig afphoto afdesign exr hdr avif")
        add(.documents, "pdf doc docx xls xlsx ppt pptx key pages numbers txt rtf rtfd md csv odt ods odp epub mobi tex html htm xml json yaml yml log eml emlx")
        add(.archives, "zip rar 7z tar gz tgz bz2 xz zst lz4 lzma cab sit sitx jar war ear cpio")
        add(.installers, "dmg iso pkg mpkg img xip sparseimage sparsebundle toast cdr ipsw")
        add(.code, "swift m mm h hpp c cc cpp cxx js ts tsx jsx mjs cjs py pyc rb go rs java kt kts scala cs php pl sh zsh bash lua r dart vue svelte css scss less sql o a obj class wasm ipynb gradle pbxproj xcassets map lock node")
        add(.virtualMachines, "vmdk vdi vhd vhdx qcow2 hdd utm pvm vmwarevm vmem nvram")
        add(.system, "dylib so framework kext plist db sqlite sqlite3 db-wal db-shm sqlite-wal sqlite-shm dat bin cache tmp car nib strings lproj metallib bundle plugin appex sys dll exe ttf otf ttc woff woff2 dict")
        return m
    }()

    static func of(_ node: FileNode) -> FileCategory {
        if node.inAppBundle { return .applications }
        if node.isDirectory { return node.nameExtension == "app" ? .applications : .other }
        let ext = node.nameExtension
        if ext.isEmpty { return .other }
        return map[ext] ?? .other
    }
}

struct CategoryStat: Identifiable {
    let category: FileCategory
    var count = 0
    var allocated: Int64 = 0
    var logical: Int64 = 0
    var id: FileCategory { category }
    func size(_ m: SizeMode) -> Int64 { m == .allocated ? allocated : logical }
}

struct ExtensionStat: Identifiable {
    let ext: String
    let category: FileCategory
    var count = 0
    var allocated: Int64 = 0
    var logical: Int64 = 0
    var id: String { ext }
    func size(_ m: SizeMode) -> Int64 { m == .allocated ? allocated : logical }
}

/// Flattened, sorted views of a scan, rebuilt in the background after scans and deletions.
struct DerivedStats {
    var files: [FileNode] = []
    var categories: [CategoryStat] = []
    var extensions: [ExtensionStat] = []
    var unreadable: [FileNode] = []
    var excluded: [FileNode] = []
    var hardlinks = 0
    var oldest: Double = 0

    static func build(root: FileNode, mode: SizeMode) -> DerivedStats {
        var d = DerivedStats()
        var cats: [FileCategory: CategoryStat] = [:]
        var exts: [String: ExtensionStat] = [:]
        var stack: [FileNode] = [root]
        while let n = stack.popLast() {
            if n.isDirectory {
                if n.unreadable { d.unreadable.append(n) }
                if n.excluded { d.excluded.append(n) }
                stack.append(contentsOf: n.children)
                continue
            }
            if n.hardlinkDuplicate { d.hardlinks += 1; continue }
            d.files.append(n)
            let c = FileCategory.of(n)
            cats[c, default: CategoryStat(category: c)].count += 1
            cats[c]!.allocated += n.allocatedSize
            cats[c]!.logical += n.logicalSize
            let e = n.nameExtension.isEmpty ? "(none)" : n.nameExtension
            if exts[e] == nil { exts[e] = ExtensionStat(ext: e, category: c) }
            exts[e]!.count += 1
            exts[e]!.allocated += n.allocatedSize
            exts[e]!.logical += n.logicalSize
        }
        d.files.sort { $0.size(mode) > $1.size(mode) }
        d.categories = cats.values.sorted { $0.size(mode) > $1.size(mode) }
        d.extensions = exts.values.sorted { $0.size(mode) > $1.size(mode) }
        return d
    }
}

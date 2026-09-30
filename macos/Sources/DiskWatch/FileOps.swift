import Foundation

struct FileOpError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

enum FileOps {
    /// Move to the real user's Trash (not root's), using the per-volume .Trashes folder off the boot volume.
    @discardableResult
    static func moveToTrash(_ path: String) throws -> String {
        let fm = FileManager.default
        if !RealUser.isRoot {
            var result: NSURL?
            try fm.trashItem(at: URL(fileURLWithPath: path), resultingItemURL: &result)
            return result?.path ?? ""
        }
        var st = stat(), hst = stat()
        guard lstat(path, &st) == 0 else { throw posix(errno, path) }
        let trashDir: String
        if stat(RealUser.home, &hst) == 0 && hst.st_dev == st.st_dev {
            trashDir = RealUser.home + "/.Trash"
            if !fm.fileExists(atPath: trashDir) {
                mkdir(trashDir, 0o700)
                RealUser.giveToUser(trashDir)
            }
        } else {
            guard let mount = MountInfo.containing(path) else { throw FileOpError(message: "Could not find the volume for \(path).") }
            let trashes = (mount.path == "/" ? "" : mount.path) + "/.Trashes"
            if !fm.fileExists(atPath: trashes) { mkdir(trashes, 0o1333) }
            trashDir = trashes + "/\(RealUser.uid)"
            if !fm.fileExists(atPath: trashDir) {
                mkdir(trashDir, 0o700)
                RealUser.giveToUser(trashDir)
            }
        }
        let name = (path as NSString).lastPathComponent
        var dest = trashDir + "/" + name
        var n = 2
        while fm.fileExists(atPath: dest) || (try? fm.destinationOfSymbolicLink(atPath: dest)) != nil {
            let base = (name as NSString).deletingPathExtension
            let ext = (name as NSString).pathExtension
            dest = trashDir + "/" + base + " \(n)" + (ext.isEmpty ? "" : "." + ext)
            n += 1
        }
        if rename(path, dest) != 0 { throw posix(errno, path) }
        return dest
    }

    static func deletePermanently(_ path: String) throws {
        var st = stat()
        guard lstat(path, &st) == 0 else { throw posix(errno, path) }
        if (st.st_mode & S_IFMT) == S_IFDIR {
            do {
                try FileManager.default.removeItem(atPath: path)
            } catch {
                throw FileOpError(message: "Some items inside could not be removed: \((error as NSError).localizedDescription)")
            }
        } else if unlink(path) != 0 {
            throw posix(errno, path)
        }
    }

    static func posix(_ code: Int32, _ path: String) -> FileOpError {
        switch code {
        case EPERM: FileOpError(message: "Operation not permitted — macOS protects this item (SIP, a lock, or missing Full Disk Access).")
        case EACCES: FileOpError(message: "Permission denied.")
        case EBUSY: FileOpError(message: "The item is in use.")
        case ENOENT: FileOpError(message: "The item no longer exists.")
        case EROFS: FileOpError(message: "It's on a read-only volume.")
        case EXDEV: FileOpError(message: "Can't move across volumes.")
        default: FileOpError(message: String(cString: strerror(code)))
        }
    }

    // MARK: - Audit log

    static var logPath: String { RealUser.home + "/Library/Logs/DiskWatch/deletions.log" }

    static func log(_ action: String, path: String, size: Int64, detail: String = "") {
        let dir = (logPath as NSString).deletingLastPathComponent
        let fm = FileManager.default
        if !fm.fileExists(atPath: dir) {
            try? fm.createDirectory(atPath: dir, withIntermediateDirectories: true)
            RealUser.giveToUser(dir)
        }
        let stamp = ISO8601DateFormatter().string(from: Date())
        let line = "\(stamp)\t\(action)\t\(Fmt.bytes(size))\t\(path)\(detail.isEmpty ? "" : "\t" + detail)\n"
        if let h = FileHandle(forWritingAtPath: logPath) {
            h.seekToEndOfFile()
            h.write(Data(line.utf8))
            try? h.close()
        } else {
            fm.createFile(atPath: logPath, contents: Data(line.utf8))
            RealUser.giveToUser(logPath)
        }
    }

    /// Write a file the user asked for (exports) and make sure they own it.
    static func writeUserFile(_ data: Data, to path: String) throws {
        try data.write(to: URL(fileURLWithPath: path))
        RealUser.giveToUser(path)
    }
}

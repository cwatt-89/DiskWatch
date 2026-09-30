import AppKit
import Foundation

/// The human who is logged in, even when DiskWatch itself is running as root.
enum RealUser {
    static let uid: uid_t = {
        let args = CommandLine.arguments
        if let i = args.firstIndex(of: "--uid"), i + 1 < args.count, let v = UInt32(args[i + 1]), v != 0 {
            return v
        }
        if getuid() != 0 { return getuid() }
        if let s = ProcessInfo.processInfo.environment["SUDO_UID"], let v = UInt32(s), v != 0 { return v }
        var st = stat()
        if stat("/dev/console", &st) == 0, st.st_uid != 0 { return st.st_uid }
        return getuid()
    }()

    private static let passwd: (name: String, home: String, gid: gid_t) = {
        guard let pw = getpwuid(uid) else { return (NSUserName(), NSHomeDirectory(), getgid()) }
        return (String(cString: pw.pointee.pw_name), String(cString: pw.pointee.pw_dir), pw.pointee.pw_gid)
    }()

    static var name: String { passwd.name }
    static var home: String { passwd.home }
    static var gid: gid_t { passwd.gid }

    static var isRoot: Bool { geteuid() == 0 }

    /// Hand a file we created as root back to the real user.
    static func giveToUser(_ path: String) {
        guard isRoot else { return }
        chown(path, uid, gid)
    }

    /// Run a command in the real user's GUI session (so Finder/apps open as them, not as root).
    @discardableResult
    static func run(_ args: [String], wait: Bool = false) -> Int32 {
        let p = Process()
        if isRoot {
            p.executableURL = URL(fileURLWithPath: "/bin/launchctl")
            p.arguments = ["asuser", String(uid), "/usr/bin/sudo", "-u", name, "--"] + args
        } else {
            p.executableURL = URL(fileURLWithPath: args[0])
            p.arguments = Array(args.dropFirst())
        }
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return -1 }
        if wait { p.waitUntilExit(); return p.terminationStatus }
        return 0
    }

    static func reveal(_ paths: [String]) {
        guard !paths.isEmpty else { return }
        run(["/usr/bin/open", "-R"] + paths)
    }

    static func open(_ path: String) { run(["/usr/bin/open", path]) }
    static func quickLook(_ path: String) { run(["/usr/bin/qlmanage", "-p", path]) }
    static func openURL(_ url: String) { run(["/usr/bin/open", url]) }
}

/// Runs a command and captures stdout (as whoever DiskWatch runs as).
func shell(_ exe: String, _ args: [String]) -> (status: Int32, out: String) {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: exe)
    p.arguments = args
    let pipe = Pipe()
    p.standardOutput = pipe
    p.standardError = pipe
    do { try p.run() } catch { return (-1, error.localizedDescription) }
    let data = pipe.fileHandleForReading.readDataToEndOfFile()
    p.waitUntilExit()
    return (p.terminationStatus, String(decoding: data, as: UTF8.self))
}

enum Elevation {
    static var launchedElevated: Bool { CommandLine.arguments.contains("--elevated") }
    /// Test hook: run the full detach/handshake path without a password prompt (the child stays non-root).
    static var testRelaunch: Bool { ProcessInfo.processInfo.environment["DISKWATCH_TEST_RELAUNCH"] != nil }
    static var bypassed: Bool {
        ProcessInfo.processInfo.environment["DISKWATCH_NO_ELEVATE"] != nil || (testRelaunch && launchedElevated)
    }

    enum Outcome { case relaunched, cancelled, failed(String) }

    /// Written by the elevated copy once its window is up; the launching copy waits for it before quitting.
    static var readyPath: String { "/private/tmp/com.carterwatt.DiskWatch.\(RealUser.uid).ready" }
    static var launchLogPath: String { "/private/tmp/com.carterwatt.DiskWatch.\(RealUser.uid).launch.log" }

    /// `DiskWatch --detach …` — leave the launching app's process group/session, then become the real app.
    /// Without this, launchd kills the elevated copy the moment the original (non-root) copy quits.
    static func detachIfRequested() {
        let args = CommandLine.arguments
        guard args.count > 1, args[1] == "--detach", let exe = Bundle.main.executablePath else { return }
        setsid()
        let newArgs = [exe, "--elevated"] + Array(args.dropFirst(2))
        let cArgs: [UnsafeMutablePointer<CChar>?] = newArgs.map { strdup($0) } + [nil]
        execv(exe, cArgs)
        perror("DiskWatch: execv failed")
        exit(1)
    }

    /// Called by the elevated copy after launch.
    static func signalReady() {
        guard launchedElevated, RealUser.isRoot || testRelaunch else { return }
        unlink(readyPath)
        FileManager.default.createFile(atPath: readyPath, contents: Data("\(getpid())".utf8))
    }

    /// Ask for an admin password, start a detached root copy of ourselves, and wait until it's running.
    static func relaunchAsRoot() -> Outcome {
        guard let exe = Bundle.main.executablePath else { return .failed("Could not locate the DiskWatch executable.") }
        func q(_ s: String) -> String { "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'" }
        unlink(readyPath)
        let started = Date().timeIntervalSince1970 - 1
        let cmd = "\(q(exe)) --detach --uid \(getuid()) </dev/null >\(q(launchLogPath)) 2>&1 &"

        if testRelaunch {
            let r = shell("/bin/sh", ["-c", cmd])
            if r.status != 0 { return .failed(r.out) }
        } else {
            func esc(_ s: String) -> String {
                s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
            }
            let source = """
            do shell script "\(esc(cmd))" with prompt "DiskWatch needs administrator access to measure and clean every file on your Mac." with administrator privileges
            """
            var err: NSDictionary?
            guard let script = NSAppleScript(source: source) else { return .failed("Could not build the authorization request.") }
            script.executeAndReturnError(&err)
            if let err {
                let code = err[NSAppleScript.errorNumber] as? Int ?? 0
                if code == -128 { return .cancelled }
                return .failed(err[NSAppleScript.errorMessage] as? String ?? "Authorization failed (\(code)).")
            }
        }

        // Wait (up to ~10 s) for the elevated copy to report that it's up.
        let deadline = Date().addingTimeInterval(10)
        while Date() < deadline {
            var st = stat()
            if lstat(readyPath, &st) == 0 && (st.st_uid == 0 || testRelaunch) && Double(st.st_mtimespec.tv_sec) >= started {
                return .relaunched
            }
            RunLoop.current.run(until: Date().addingTimeInterval(0.1))
        }
        let log = (try? String(contentsOfFile: launchLogPath, encoding: .utf8))?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return .failed("The administrator copy of DiskWatch didn't start." + (log.isEmpty ? "" : "\n\n" + String(log.suffix(600))))
    }
}

enum FullDiskAccess {
    /// Reading the TCC database requires Full Disk Access, even for root.
    static var granted: Bool {
        let probes = ["/Library/Application Support/com.apple.TCC/TCC.db",
                      RealUser.home + "/Library/Safari/Bookmarks.plist",
                      RealUser.home + "/Library/Application Support/com.apple.TCC/TCC.db"]
        for p in probes where FileManager.default.fileExists(atPath: p) {
            let fd = Darwin.open(p, O_RDONLY)
            if fd >= 0 { close(fd); return true }
            return false
        }
        return true
    }

    static func openSettings() {
        RealUser.openURL("x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_AllFiles")
    }
}

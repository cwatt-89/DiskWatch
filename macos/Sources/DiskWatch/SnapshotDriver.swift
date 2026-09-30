import AppKit

/// Debug aid: `DISKWATCH_SNAPSHOT=<dir>` scans a folder, visits every tab and writes window PNGs, then quits.
/// Uses the window's own backing store, so it needs no Screen Recording permission.
@MainActor
enum SnapshotDriver {
    static var outDir: String? { ProcessInfo.processInfo.environment["DISKWATCH_SNAPSHOT"] }

    static func start(app: AppState) {
        guard let dir = outDir else { return }
        let path = ProcessInfo.processInfo.environment["DISKWATCH_SNAPSHOT_PATH"] ?? RealUser.home + "/Dev"
        Task { @MainActor in
            try? await Task.sleep(for: .seconds(1))
            capture(dir, "0-welcome")
            app.scan(path)
            try? await Task.sleep(for: .milliseconds(150))
            capture(dir, "1-scanning")
            while app.isScanning { try? await Task.sleep(for: .milliseconds(100)) }
            try? await Task.sleep(for: .seconds(1))
            if let big = app.root?.children.first { app.select(big) }
            for tab in MainTab.allCases {
                app.tab = tab
                if tab == .cleanup { app.cleanup.scanAll(); while app.cleanup.isMeasuring { try? await Task.sleep(for: .milliseconds(200)) } }
                if tab == .duplicates {
                    app.duplicates.minSize = 1000
                    app.duplicates.run(files: app.derived.files)
                    while app.duplicates.isRunning { try? await Task.sleep(for: .milliseconds(200)) }
                }
                try? await Task.sleep(for: .seconds(3))
                capture(dir, "2-\(tab.rawValue)")
            }
            app.tab = .tree
            // A mix of safety levels, including a protected item, to exercise the confirmation.
            let picks = [app.derived.files.first, app.root?.find(path: RealUser.home + "/Library"), app.root?.find(path: RealUser.home + "/Downloads")].compactMap { $0 }
            app.requestDelete(picks.isEmpty ? Array(app.derived.files.prefix(2)) : picks, permanent: true)
            try? await Task.sleep(for: .seconds(1.5))
            capture(dir, "3-delete")
            app.deletionRequest = nil
            try? await Task.sleep(for: .seconds(0.5))
            NSApp.terminate(nil)
        }
    }

    static func capture(_ dir: String, _ name: String) {
        guard let w = NSApp.windows.first(where: { $0.isVisible && !$0.isSheet && $0.frame.width > 500 }) else { return }
        save(w, dir, name)
    }

    static func save(_ w: NSWindow, _ dir: String, _ name: String) {
        guard let view = w.contentView?.superview ?? w.contentView else { return }
        view.layoutSubtreeIfNeeded()
        view.displayIfNeeded()
        guard let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return }
        view.cacheDisplay(in: view.bounds, to: rep)
        try? rep.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: "\(dir)/\(name).png"))
    }
}

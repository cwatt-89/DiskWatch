import AppKit
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
        NSApp.appearance = NSAppearance(named: .darkAqua)
        NSApp.activate(ignoringOtherApps: true)
        DispatchQueue.main.async {
            for w in NSApp.windows {
                w.isMovableByWindowBackground = true
                w.backgroundColor = FN.NS.bg
            }
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
            NSApp.activate(ignoringOtherApps: true)
            Elevation.signalReady()
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

@main
enum Entry {
    static func main() {
        Elevation.detachIfRequested()
        if CommandLine.arguments.contains("--selftest") {
            SelfTest.run()
            exit(0)
        }
        DiskWatchApp.main()
    }
}

struct DiskWatchApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @State private var app = AppState()

    var body: some Scene {
        Window("DiskWatch", id: "main") {
            RootView()
                .environment(app)
                .frame(minWidth: 1040, minHeight: 640)
        }
        .defaultSize(width: 1440, height: 900)
        .windowStyle(.hiddenTitleBar)
        .commands { DiskWatchCommands(app: app) }

        Settings {
            SettingsView().environment(app)
        }
    }
}

struct DiskWatchCommands: Commands {
    let app: AppState

    var body: some Commands {
        CommandGroup(replacing: .newItem) {
            Button("Scan Folder…") { app.chooseFolder() }.keyboardShortcut("o")
            Button("Scan Home Folder") { app.scan(RealUser.home) }.keyboardShortcut("h", modifiers: [.command, .shift])
            Button("Scan Macintosh HD") { app.scan("/") }.keyboardShortcut("d", modifiers: [.command, .shift])
            Divider()
            Button("Rescan") { app.rescan() }.keyboardShortcut("r")
            Button("Stop Scan") { app.cancelScan() }.keyboardShortcut(".")
            Divider()
            Button("Export Report…") { app.exportCSV() }.keyboardShortcut("e")
            Button("Show Deletion Log") { RealUser.reveal([FileOps.logPath]) }
        }
        CommandMenu("Item") {
            Button("Reveal in Finder") { RealUser.reveal(app.selection.map(\.path)) }
                .keyboardShortcut("r", modifiers: [.command, .shift])
            Button("Quick Look") { if let n = app.selection.first { RealUser.quickLook(n.path) } }
                .keyboardShortcut("y")
            Button("Rescan Selected Folder") { if let n = app.selection.first { app.rescan(n) } }
                .keyboardShortcut("r", modifiers: [.command, .option])
            Button("Show in Treemap") { if let n = app.selection.first { app.showInTreemap(n) } }
            Divider()
            Button("Move to Trash…") { app.requestDelete(app.selection, permanent: false) }
                .keyboardShortcut(.delete, modifiers: .command)
            Button("Delete Permanently…") { app.requestDelete(app.selection, permanent: true) }
                .keyboardShortcut(.delete, modifiers: [.command, .option])
        }
        CommandGroup(after: .sidebar) {
            ForEach(Array(MainTab.allCases.enumerated()), id: \.offset) { i, t in
                Button(t.title) { app.tab = t }.keyboardShortcut(KeyEquivalent(Character("\(i + 1)")), modifiers: .command)
            }
            Divider()
            Button("Toggle Inspector") { app.showInspector.toggle() }.keyboardShortcut("i", modifiers: [.command, .option])
            Button("Toggle Size Mode") { app.sizeMode = app.sizeMode.other }.keyboardShortcut("s", modifiers: [.command, .shift])
        }
    }
}

struct SettingsView: View {
    @Environment(AppState.self) private var app
    @State private var newExclusion = ""

    var body: some View {
        @Bindable var app = app
        ScrollView {
            VStack(alignment: .leading, spacing: FN.s5) {
                FNSection(title: "Scanning") {
                    VStack(alignment: .leading, spacing: FN.s3) {
                        Toggle("Stay on the scanned volume (skip other disks & network mounts)", isOn: $app.stayOnVolume)
                            .toggleStyle(.fnCheckbox)
                        HStack {
                            Text("Default size").fnLabel()
                            Spacer()
                            FNSegmented(selection: $app.sizeMode, items: SizeMode.allCases.map { ($0, $0.title) })
                        }
                    }
                }
                FNSection(title: "Excluded", caption: "Folders skipped on every scan. Right-click a folder → Exclude from Future Scans.") {
                    VStack(alignment: .leading, spacing: 0) {
                        ForEach(app.excludedPaths, id: \.self) { p in
                            HStack {
                                Text(p).fnData().lineLimit(1).truncationMode(.middle)
                                Spacer()
                                Button { app.excludedPaths.removeAll { $0 == p } } label: { Image(systemName: "xmark") }
                                    .buttonStyle(.fnGhost)
                            }
                            .padding(.vertical, 6)
                            .overlay(alignment: .bottom) { FNRule() }
                        }
                        HStack(spacing: FN.s1) {
                            FNTextField(placeholder: "/path/to/exclude", text: $newExclusion)
                            Button("Add") {
                                let p = newExclusion.trimmingCharacters(in: .whitespaces)
                                if !p.isEmpty { app.excludeFromScans(p); newExclusion = "" }
                            }
                            .buttonStyle(.fnSecondary)
                        }
                        .padding(.top, FN.s2)
                    }
                }
                FNSection(title: "Deleting") {
                    VStack(alignment: .leading, spacing: FN.s3) {
                        HStack {
                            Text("Default action").fnLabel()
                            Spacer()
                            FNSegmented(selection: $app.defaultPermanent, items: [(false, "Move to Trash"), (true, "Delete permanently")])
                        }
                        Text("Protected items can never be removed. Dangerous items always require typing DELETE. Every action is logged to \(Fmt.abbreviate(FileOps.logPath)).")
                            .fnCaption().fixedSize(horizontal: false, vertical: true)
                    }
                }
                FNSection(title: "Privileges") {
                    VStack(alignment: .leading, spacing: FN.s2) {
                        HStack {
                            Text("Running as").fnLabel()
                            Spacer()
                            FNPill(text: RealUser.isRoot ? "root · \(RealUser.name)" : RealUser.name, state: RealUser.isRoot ? .ok : .warn)
                        }
                        HStack {
                            Text("Full Disk Access").fnLabel()
                            Spacer()
                            FNPill(text: app.fullDiskAccess ? "Granted" : "Not granted", state: app.fullDiskAccess ? .ok : .warn)
                        }
                        Button("Open Full Disk Access Settings") { FullDiskAccess.openSettings() }.buttonStyle(.fnSecondary)
                    }
                }
            }
            .padding(FN.s5)
        }
        .frame(width: 600, height: 640)
        .background(FN.bg)
        .preferredColorScheme(.dark)
    }
}

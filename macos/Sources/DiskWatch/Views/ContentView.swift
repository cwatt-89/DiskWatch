import SwiftUI

struct RootView: View {
    @Environment(AppState.self) private var app

    var body: some View {
        Group {
            if app.needsElevation {
                ElevationView()
            } else {
                ContentView()
            }
        }
        .background(FN.bg)
        .preferredColorScheme(.dark)
        .tint(FN.accent)
        .onAppear {
            if !RealUser.isRoot && !Elevation.bypassed { app.needsElevation = true }
            SnapshotDriver.start(app: app)
        }
    }
}

struct ContentView: View {
    @Environment(AppState.self) private var app

    var body: some View {
        ZStack {
            HStack(spacing: 0) {
                SidebarView().frame(width: 256)
                FNRule(vertical: true)
                VStack(spacing: 0) {
                    HeaderBar()
                    FNRule()
                    if app.root == nil && !app.isScanning {
                        WelcomeView()
                    } else {
                        if !app.fullDiskAccess { FDABanner() }
                        if app.scanWasCancelled {
                            Banner(color: FN.warn, symbol: "exclamationmark.triangle",
                                   text: "Scan stopped early — totals only include what was read before stopping.") {
                                Button("Rescan") { app.rescan() }.buttonStyle(.fn(.secondary, compact: true))
                            }
                        }
                        tabContent
                            .frame(maxWidth: .infinity, maxHeight: .infinity)
                        FNRule()
                        StatusBar()
                    }
                }
                .frame(maxWidth: .infinity)
                if app.showInspector && app.root != nil {
                    FNRule(vertical: true)
                    InspectorView().frame(width: 320)
                }
            }
            if app.isScanning { ScanProgressOverlay() }
            if let req = app.deletionRequest {
                FNModal(width: 660) { DeleteConfirmView(request: req) }
                    .id(req.id)
            }
        }
        .background(FN.bg)
        .alert("DiskWatch", isPresented: Binding(get: { app.errorMessage != nil }, set: { if !$0 { app.errorMessage = nil } })) {
            Button("OK") { app.errorMessage = nil }
        } message: {
            Text(app.errorMessage ?? "")
        }
    }

    @ViewBuilder
    private var tabContent: some View {
        switch app.tab {
        case .overview: OverviewView()
        case .tree:
            TreeOutlineView(app: app, root: app.root, treeVersion: app.treeVersion, sortStamp: app.sortStamp,
                            sizeMode: app.sizeMode, selection: app.selection)
        case .treemap: TreemapView()
        case .files: FilesView()
        case .types: TypesView()
        case .cleanup: CleanupView()
        case .duplicates: DuplicatesView()
        }
    }
}

/// Replaces the native toolbar: location + actions on top, tab strip beneath.
struct HeaderBar: View {
    @Environment(AppState.self) private var app

    var body: some View {
        @Bindable var app = app
        VStack(alignment: .leading, spacing: FN.s3) {
            HStack(alignment: .center, spacing: FN.s2) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(app.isScanning ? "Scanning" : (app.root == nil ? "No scan" : "Location")).fnLabel()
                    Text(app.scanPath.map { Fmt.abbreviate($0) } ?? "Choose a volume or folder")
                        .fnBody().lineLimit(1).truncationMode(.middle)
                }
                Spacer(minLength: FN.s3)
                FNTextField(placeholder: "Search files", text: $app.filesFilter.text, symbol: "magnifyingglass", width: 200)
                    .disabled(app.root == nil)
                FNMenuPicker(selection: $app.sizeMode, options: SizeMode.allCases.map { ($0, $0.title) })
                    .help("Size on Disk counts allocated blocks (what you actually get back). File Size is the logical length.")
                if app.isScanning {
                    Button { app.cancelScan() } label: { Label("Stop", systemImage: "stop.fill") }
                        .buttonStyle(.fn(.danger))
                } else {
                    Button { app.rescan() } label: { Label("Rescan", systemImage: "arrow.clockwise") }
                        .buttonStyle(.fn(.secondary))
                        .disabled(app.scanPath == nil)
                        .help("Rescan (⌘R)")
                }
                Button { app.exportCSV() } label: { Image(systemName: "square.and.arrow.up") }
                    .buttonStyle(.fn(.secondary))
                    .disabled(app.root == nil)
                    .help("Export a CSV report")
                Button { app.showInspector.toggle() } label: { Image(systemName: "sidebar.right") }
                    .buttonStyle(.fn(app.showInspector ? .primary : .secondary))
                    .help("Show or hide the inspector (⌥⌘I)")
            }
            FNSegmented(selection: $app.tab, items: MainTab.allCases.map { ($0, $0.title) }, disabled: app.root == nil)
        }
        .padding(.horizontal, FN.s5)
        .padding(.top, FN.s3)
        .padding(.bottom, FN.s3)
        .onChange(of: app.filesFilter.text) { _, t in
            if !t.isEmpty && app.tab != .files && app.root != nil { app.tab = .files }
        }
    }
}

struct Banner<Trailing: View>: View {
    let color: Color
    let symbol: String
    let text: String
    @ViewBuilder var trailing: Trailing

    var body: some View {
        HStack(spacing: FN.s2) {
            Image(systemName: symbol).font(.system(size: 12, weight: .semibold)).foregroundStyle(color)
            Text(text).fnCaption(FN.fg).fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: FN.s2)
            trailing
        }
        .padding(.horizontal, FN.s5).padding(.vertical, 10)
        .background(FN.bg)
        .overlay(alignment: .bottom) { Rectangle().fill(color).frame(height: 1) }
    }
}

struct FDABanner: View {
    @Environment(AppState.self) private var app

    var body: some View {
        Banner(color: FN.warn, symbol: "lock",
               text: "No Full Disk Access — Mail, Messages, Safari and app containers can't be measured or cleaned.") {
            Button("Open Settings") { FullDiskAccess.openSettings() }.buttonStyle(.fn(.secondary, compact: true))
            Button("Re-check") { app.fullDiskAccess = FullDiskAccess.granted }.buttonStyle(.fn(.secondary, compact: true))
        }
    }
}

struct StatusBar: View {
    @Environment(AppState.self) private var app

    var body: some View {
        HStack(spacing: FN.s4) {
            if let r = app.root {
                Text(Fmt.bytes(r.size(app.sizeMode))).foregroundStyle(FN.fg)
                Text("\(Fmt.count(r.fileCount)) files")
                Text("\(Fmt.count(r.dirCount)) folders")
                if !app.derived.unreadable.isEmpty {
                    Text("\(app.derived.unreadable.count) unreadable").foregroundStyle(FN.warn)
                        .help("Folders that couldn't be opened. Grant Full Disk Access to include them.")
                }
                if app.isRebuilding { Text("updating…") }
            }
            Spacer()
            if app.selection.count > 0 {
                let total = app.selection.filter { n in !app.selection.contains { n.isDescendant(of: $0) } }
                    .reduce(Int64(0)) { $0 + $1.size(app.sizeMode) }
                Text("\(app.selection.count) selected · \(Fmt.bytes(total))").foregroundStyle(FN.fg).fixedSize()
            }
            FNPill(text: RealUser.isRoot ? "root" : RealUser.name, state: RealUser.isRoot ? .ok : .neutral)
        }
        .font(FN.mono(11))
        .foregroundStyle(FN.muted)
        .lineLimit(1)
        .fixedSize(horizontal: false, vertical: true)
        .padding(.horizontal, FN.s5).padding(.vertical, 7)
    }
}

struct ScanProgressOverlay: View {
    @Environment(AppState.self) private var app

    var body: some View {
        FNModal(width: 560) {
            VStack(alignment: .leading, spacing: FN.s5) {
                FNSectionHeading(title: "Scanning", caption: Fmt.abbreviate(app.scanPath ?? ""))
                TimelineView(.periodic(from: .now, by: 0.25)) { ctx in
                    let p = app.progress
                    let elapsed = ctx.date.timeIntervalSince(app.scanStarted)
                    VStack(alignment: .leading, spacing: FN.s5) {
                        FNHeroValue(text: Fmt.bytes(p.bytes))
                        HStack(spacing: FN.s3) {
                            FNStatTile(label: "Files", value: Fmt.count(p.files))
                            FNStatTile(label: "Folders", value: Fmt.count(p.dirs))
                            FNStatTile(label: "Elapsed", value: Fmt.duration(elapsed))
                        }
                        HStack {
                            Text(elapsed > 1 ? "\(Fmt.count(Int(Double(p.files) / max(elapsed, 0.1)))) files/sec" : "starting…").fnCaption()
                            Spacer()
                            if p.errors > 0 { FNPill(text: "\(p.errors) unreadable", state: .warn) }
                        }
                        // Indeterminate: a sliding accent block in a sharp track.
                        GeometryReader { g in
                            let t = elapsed.truncatingRemainder(dividingBy: 1.6) / 1.6
                            ZStack(alignment: .leading) {
                                Rectangle().fill(FN.track)
                                Rectangle().fill(FN.accent).frame(width: g.size.width * 0.18)
                                    .offset(x: (g.size.width * 1.18) * t - g.size.width * 0.18)
                            }
                            .clipped()
                            .overlay(Rectangle().strokeBorder(FN.hair, lineWidth: 1))
                        }
                        .frame(height: 6)
                    }
                }
                Text(Fmt.abbreviate(app.progress.path))
                    .fnCaption().lineLimit(1).truncationMode(.middle)
                HStack {
                    Spacer()
                    Button("Stop Scan") { app.cancelScan() }
                        .buttonStyle(.fnDanger)
                        .keyboardShortcut(".", modifiers: .command)
                }
            }
        }
    }
}

struct WelcomeView: View {
    @Environment(AppState.self) private var app

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: FN.s7) {
                VStack(alignment: .leading, spacing: FN.s2) {
                    FNWordmark()
                    Text("See exactly where your disk space went — and safely take it back.").fnBody(FN.muted)
                }
                FNSection(title: "Volumes", caption: "Pick a volume to scan everything on it.") {
                    VStack(spacing: FN.s5) {
                        ForEach(app.volumes) { v in VolumeCapacity(volume: v, hero: v.path == "/") }
                    }
                }
                HStack(spacing: FN.s2) {
                    Button { app.scan(RealUser.home) } label: { Label("Scan Home Folder", systemImage: "house") }
                        .buttonStyle(.fnPrimary)
                    Button { app.chooseFolder() } label: { Label("Choose Folder…", systemImage: "folder") }
                        .buttonStyle(.fnSecondary)
                }
                FNSection(title: "Safety levels", caption: "Every item is classified before anything can be removed.") {
                    VStack(alignment: .leading, spacing: FN.s3) {
                        ForEach(SafetyLevel.allCases) { l in
                            HStack(alignment: .firstTextBaseline, spacing: FN.s3) {
                                FNPill(text: l.shortTitle, state: l.pill).frame(width: 100, alignment: .leading)
                                Text(l.explanation).fnData(FN.muted).fixedSize(horizontal: false, vertical: true)
                            }
                        }
                    }
                }
            }
            .padding(FN.s7)
            .frame(maxWidth: 820, alignment: .leading)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .background(FN.bg)
    }
}

/// Volume readout: capacity gauge with a 90% ceiling marker, per the Gauge component.
struct VolumeCapacity: View {
    let volume: VolumeInfo
    var hero = false
    @Environment(AppState.self) private var app

    var body: some View {
        let state = FNGaugeState.of(volume.fraction, threshold: 0.9)
        Button { app.scan(volume.path) } label: {
            VStack(alignment: .leading, spacing: 10) {
                HStack(alignment: .lastTextBaseline, spacing: FN.s2) {
                    if hero {
                        FNHeroValue(text: "\(Int((volume.fraction * 100).rounded()))%")
                    } else {
                        Text("\(Int((volume.fraction * 100).rounded()))%").fnValue()
                    }
                    Text("\(volume.name) used").fnCaption()
                    Spacer()
                    FNPill(text: state == .ok ? "Healthy" : state == .warn ? "Filling up" : "Nearly full", state: state.pill)
                }
                FNGauge(fraction: volume.fraction)
                HStack {
                    Text("0%")
                    Spacer()
                    Text("\(Fmt.bytes(volume.available)) free of \(Fmt.bytes(volume.total)) · ceiling 90%")
                    Spacer()
                    Text("100%")
                }
                .font(FN.mono(11)).foregroundStyle(FN.muted)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

struct ElevationView: View {
    @Environment(AppState.self) private var app
    @State private var status: String?
    @State private var working = false
    @State private var attempted = false

    var body: some View {
        VStack(alignment: .leading, spacing: FN.s5) {
            FNWordmark()
            FNSection(title: "Administrator access", caption: "Required every launch.") {
                VStack(alignment: .leading, spacing: FN.s5) {
                    Text("DiskWatch runs as an administrator so it can measure and clean every folder on your Mac. Protected system files stay locked no matter what.")
                        .fnBody(FN.muted).fixedSize(horizontal: false, vertical: true)
                    if let status {
                        Text(status).fnData(FN.danger).fixedSize(horizontal: false, vertical: true).textSelection(.enabled)
                    }
                    HStack(spacing: FN.s2) {
                        Button {
                            authenticate()
                        } label: {
                            Text(working ? "Waiting…" : "Authenticate")
                        }
                        .buttonStyle(.fnPrimary)
                        .keyboardShortcut(.defaultAction)
                        .disabled(working)
                        Button("Quit") { NSApp.terminate(nil) }.buttonStyle(.fnSecondary)
                    }
                }
            }
            .frame(maxWidth: 560)
        }
        .padding(FN.s8)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(FN.bg)
        .task {
            guard !attempted else { return }
            attempted = true
            if Elevation.launchedElevated && !Elevation.bypassed {
                status = "DiskWatch was relaunched but still isn't running as root. Try again, or check that your account is an administrator."
                return
            }
            try? await Task.sleep(for: .milliseconds(400))
            authenticate()
        }
    }

    private func authenticate() {
        working = true
        status = nil
        DispatchQueue.main.async {
            switch Elevation.relaunchAsRoot() {
            case .relaunched:
                NSApp.terminate(nil)
            case .cancelled:
                status = "Authentication was cancelled. DiskWatch needs administrator access to run."
            case .failed(let msg):
                status = msg
            }
            working = false
        }
    }
}

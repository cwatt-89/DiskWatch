import SwiftUI

struct SidebarView: View {
    @Environment(AppState.self) private var app

    private var places: [(String, String, String)] {
        let h = RealUser.home
        var p: [(String, String, String)] = [
            ("Home", h, "house"),
            ("Applications", "/Applications", "square.grid.2x2"),
            ("Downloads", h + "/Downloads", "arrow.down.to.line"),
            ("Documents", h + "/Documents", "doc"),
            ("Desktop", h + "/Desktop", "menubar.rectangle"),
            ("User Library", h + "/Library", "books.vertical"),
            ("System Library", "/Library", "building.columns"),
        ]
        if FileManager.default.fileExists(atPath: h + "/Library/Developer") {
            p.append(("Developer", h + "/Library/Developer", "hammer"))
        }
        p.append(("Users", "/Users", "person.2"))
        return p
    }

    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: FN.s6) {
                    FNWordmark()
                        .padding(.top, FN.s7)
                        .padding(.horizontal, FN.s4)

                    group("Volumes") {
                        ForEach(app.volumes) { v in
                            SidebarVolumeRow(volume: v, active: app.scanPath == v.path) { app.scan(v.path) }
                                .contextMenu { Button("Reveal in Finder") { RealUser.reveal([v.path]) } }
                        }
                    }
                    group("Places") {
                        ForEach(places, id: \.1) { name, path, symbol in
                            SidebarRow(title: name, symbol: symbol, active: app.scanPath == path) { app.scan(path) }
                                .help(path)
                        }
                    }
                    if !app.recentPaths.isEmpty {
                        group("Recent") {
                            ForEach(app.recentPaths, id: \.self) { p in
                                SidebarRow(title: Fmt.abbreviate(p), symbol: "clock", active: false) { app.scan(p) }
                                    .help(p)
                            }
                        }
                    }
                    group("Status") {
                        VStack(alignment: .leading, spacing: FN.s2) {
                            HStack {
                                Text("Privileges").fnData(FN.muted)
                                Spacer()
                                FNPill(text: RealUser.isRoot ? "Root" : "Limited", state: RealUser.isRoot ? .ok : .warn)
                            }
                            HStack {
                                Text("Disk access").fnData(FN.muted)
                                Spacer()
                                if app.fullDiskAccess {
                                    FNPill(text: "Full", state: .ok)
                                } else {
                                    Button("Fix") { FullDiskAccess.openSettings() }.buttonStyle(.fn(.secondary, compact: true))
                                    FNPill(text: "Partial", state: .warn)
                                }
                            }
                        }
                        .padding(.horizontal, FN.s4)
                        .help(RealUser.isRoot ? "Running with administrator privileges on behalf of \(RealUser.name)." : "Running without administrator privileges.")
                    }
                }
                .padding(.bottom, FN.s5)
            }
            FNRule()
            Button { app.chooseFolder() } label: {
                Label("Scan Folder…", systemImage: "folder.badge.plus").frame(maxWidth: .infinity)
            }
            .buttonStyle(.fnPrimary)
            .padding(FN.s4)
        }
        .background(FN.bg)
    }

    private func group<C: View>(_ title: String, @ViewBuilder _ content: () -> C) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title).fnLabel().padding(.horizontal, FN.s4).padding(.bottom, 4)
            content()
        }
    }
}

struct SidebarRow: View {
    let title: String
    let symbol: String
    let active: Bool
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: FN.s2) {
                Image(systemName: symbol).font(.system(size: 11)).frame(width: 16)
                Text(title).font(FN.mono(12)).lineLimit(1).truncationMode(.middle)
                Spacer(minLength: 0)
            }
            .foregroundStyle(active ? FN.bg : (hover ? FN.fg : FN.fg.opacity(0.85)))
            .padding(.horizontal, FN.s4).padding(.vertical, 6)
            .background(active ? FN.accent : (hover ? FN.track : .clear))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }
}

struct SidebarVolumeRow: View {
    let volume: VolumeInfo
    let active: Bool
    let action: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: FN.s2) {
                    Image(systemName: volume.isInternal ? "internaldrive" : "externaldrive").font(.system(size: 11)).frame(width: 16)
                    Text(volume.name).font(FN.mono(12, .semibold)).lineLimit(1)
                    Spacer()
                    Text("\(Int((volume.fraction * 100).rounded()))%").font(FN.mono(11))
                }
                FNGauge(fraction: volume.fraction, threshold: 0.9, height: 8)
                Text("\(Fmt.bytes(volume.available)) free / \(Fmt.bytes(volume.total))").font(FN.mono(10))
                    .foregroundStyle(active ? FN.bg : FN.muted)
            }
            .foregroundStyle(active ? FN.bg : FN.fg)
            .padding(.horizontal, FN.s4).padding(.vertical, FN.s1)
            .background(active ? FN.accent : (hover ? FN.track : .clear))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }
}

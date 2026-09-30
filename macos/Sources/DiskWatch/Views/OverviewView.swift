import SwiftUI

struct OverviewView: View {
    @Environment(AppState.self) private var app

    var body: some View {
        if let root = app.root {
            ScrollView {
                VStack(alignment: .leading, spacing: FN.s5) {
                    diskSection(root)
                    statGrid(root)
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 380), spacing: FN.s5, alignment: .top)], spacing: FN.s5) {
                        topItems(root)
                        kinds
                        largestFiles
                        stale
                        cleanup
                        if !app.derived.unreadable.isEmpty || !app.fullDiskAccess { access }
                    }
                }
                .padding(FN.s5)
            }
            .background(FN.bg)
        }
    }

    // MARK: Sections

    private func diskSection(_ root: FileNode) -> some View {
        FNSection(title: "Disk", caption: app.rootVolume.map { "\($0.name) · \(Fmt.bytes($0.total)) volume" }) {
            if let v = app.rootVolume {
                let scanned = root.allocatedSize
                let isVolumeScan = SafetyClassifier.normalize(app.scanPath ?? "") == v.path
                let state = FNGaugeState.of(v.fraction, threshold: 0.9)
                VStack(alignment: .leading, spacing: FN.s3) {
                    HStack(alignment: .lastTextBaseline, spacing: FN.s2) {
                        FNHeroValue(text: "\(Int((v.fraction * 100).rounded()))%")
                        Text("Volume capacity used").fnCaption()
                        Spacer()
                        FNPill(text: state == .ok ? "Healthy" : state == .warn ? "Filling up" : "Nearly full", state: state.pill)
                    }
                    FNGauge(fraction: v.fraction, threshold: 0.9,
                            color: nil, secondary: nil)
                        .overlay(alignment: .leading) {
                            // This scan's share, drawn as a bright segment inside the used fill.
                            GeometryReader { g in
                                Rectangle().fill(FN.fg)
                                    .frame(width: g.size.width * min(v.fraction, Double(scanned) / Double(max(1, v.total))), height: 4)
                                    .offset(y: g.size.height - 5)
                            }
                        }
                    HStack {
                        Text("0%")
                        Spacer()
                        Text("ceiling 90%")
                        Spacer()
                        Text("100%")
                    }
                    .font(FN.mono(11)).foregroundStyle(FN.muted)
                    HStack(spacing: FN.s5) {
                        legend(FN.fg, "This scan", Fmt.bytes(scanned))
                        legend(FNGaugeState.of(v.fraction, threshold: 0.9).color, "Used", Fmt.bytes(v.used))
                        legend(FN.track, "Free", Fmt.bytes(v.available))
                    }
                    if isVolumeScan && v.used > scanned {
                        Text("\(Fmt.bytes(v.used - scanned)) in use isn't visible as files: local Time Machine snapshots, purgeable space, APFS metadata, other volumes in the container, or folders DiskWatch couldn't read.")
                            .fnCaption().fixedSize(horizontal: false, vertical: true)
                    }
                }
            } else {
                Text("Volume information unavailable.").fnCaption()
            }
        }
    }

    private func statGrid(_ root: FileNode) -> some View {
        let files = app.derived.files
        let avg = files.isEmpty ? 0 : root.logicalSize / Int64(files.count)
        let biggest = root.children.max { $0.size(app.sizeMode) < $1.size(app.sizeMode) }
        return LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: FN.s5), count: 4), spacing: FN.s5) {
            FNStatTile(label: "Size on disk", value: Fmt.bytes(root.allocatedSize))
            FNStatTile(label: "File size", value: Fmt.bytes(root.logicalSize))
            FNStatTile(label: "Files", value: Fmt.count(root.fileCount))
            FNStatTile(label: "Folders", value: Fmt.count(root.dirCount))
            FNStatTile(label: "Average file", value: Fmt.bytes(avg))
            FNStatTile(label: "Largest item", value: biggest.map { Fmt.bytes($0.size(app.sizeMode)) } ?? "—", detail: biggest?.name)
            FNStatTile(label: "Scan time", value: Fmt.duration(app.scanDuration), detail: app.scanWasCancelled ? "stopped early" : nil)
            FNStatTile(label: "Unreadable", value: Fmt.count(app.derived.unreadable.count),
                       valueColor: app.derived.unreadable.isEmpty ? FN.fg : FN.warn)
        }
    }

    private func topItems(_ root: FileNode) -> some View {
        let mode = app.sizeMode
        let top = Array(root.children.sorted { $0.size(mode) > $1.size(mode) }.prefix(10))
        let maxSize = Double(max(1, top.first?.size(mode) ?? 1))
        return FNSection(title: "Biggest items", caption: "Top level of this scan") {
            Button("Open Tree") { app.tab = .tree }.buttonStyle(FNLinkStyle())
        } content: {
            VStack(spacing: FN.s2) {
                ForEach(top, id: \.id) { n in
                    Button { app.showInTree(n) } label: {
                        VStack(alignment: .leading, spacing: 5) {
                            HStack(spacing: FN.s1) {
                                Image(systemName: FNIcon.symbol(for: n)).font(.system(size: 10)).foregroundStyle(FN.muted).frame(width: 14)
                                Text(n.name).fnData().lineLimit(1).truncationMode(.middle)
                                Spacer()
                                Text(Fmt.bytes(n.size(mode))).fnData()
                                Text(Fmt.percent(n.fraction(ofParent: mode))).font(FN.mono(11)).foregroundStyle(FN.muted)
                                    .frame(width: 50, alignment: .trailing)
                            }
                            FNBar(fraction: Double(n.size(mode)) / maxSize, color: n.isDirectory ? FN.accent : FileCategory.of(n).color, height: 4)
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .contextMenu { NodeMenuButtons(nodes: [n]) }
                }
            }
        }
    }

    private var kinds: some View {
        let mode = app.sizeMode
        let cats = app.derived.categories
        let total = max(1, cats.reduce(Int64(0)) { $0 + $1.size(mode) })
        return FNSection(title: "What's using space", caption: "By kind of file") {
            Button("Open Types") { app.tab = .types }.buttonStyle(FNLinkStyle())
        } content: {
            VStack(alignment: .leading, spacing: FN.s3) {
                CompositionBar(segments: cats.map { ($0.category.color, Double($0.size(mode)) / Double(total)) })
                VStack(spacing: 6) {
                    ForEach(cats.prefix(8)) { c in
                        HStack(spacing: FN.s1) {
                            Rectangle().fill(c.category.color).frame(width: 9, height: 9)
                            Text(c.category.title).fnData()
                            Spacer()
                            Text(Fmt.bytes(c.size(mode))).fnData()
                            Text(Fmt.percent(Double(c.size(mode)) / Double(total))).font(FN.mono(11)).foregroundStyle(FN.muted)
                                .frame(width: 50, alignment: .trailing)
                        }
                    }
                }
            }
        }
    }

    private var largestFiles: some View {
        let mode = app.sizeMode
        return FNSection(title: "Largest files", caption: "Anywhere in this scan") {
            Button("All Files") { app.filesFilter = FilesFilter(); app.tab = .files }.buttonStyle(FNLinkStyle())
        } content: {
            VStack(spacing: 0) {
                ForEach(Array(app.derived.files.prefix(8)), id: \.id) { f in fileLine(f, detail: Fmt.bytes(f.size(mode))) }
            }
        }
    }

    private var stale: some View {
        let mode = app.sizeMode
        let cutoff = Date().timeIntervalSince1970 - 365 * 86400
        let items = Array(app.derived.files.lazy.filter { $0.modified < cutoff && $0.size(mode) >= 50_000_000 && !$0.inAppBundle }.prefix(6))
        return FNSection(title: "Untouched", caption: "50 MB+ and not modified for a year") {
            Button("Show More") {
                app.filesFilter = FilesFilter(minSize: 50_000_000, olderThanDays: 365)
                app.tab = .files
            }
            .buttonStyle(FNLinkStyle())
        } content: {
            if items.isEmpty {
                Text("No large files older than a year.").fnCaption()
            } else {
                VStack(spacing: 0) {
                    ForEach(items, id: \.id) { f in fileLine(f, detail: Fmt.bytes(f.size(mode)) + " · " + Fmt.shortDate(f.modified)) }
                }
            }
        }
    }

    private var cleanup: some View {
        let c = app.cleanup
        return FNSection(title: "Cleanup", caption: "Caches, logs & leftovers") {
            Button(c.hasScanned ? "Open Cleanup" : "Analyze") {
                if !c.hasScanned { c.scanAll() }
                app.tab = .cleanup
            }
            .buttonStyle(FNLinkStyle())
        } content: {
            if c.hasScanned {
                VStack(alignment: .leading, spacing: FN.s3) {
                    HStack(alignment: .lastTextBaseline, spacing: FN.s1) {
                        Text(Fmt.bytes(c.totalFound)).fnValue(FN.accent)
                        Text(c.isMeasuring ? "found so far…" : "reclaimable").fnCaption()
                    }
                    let top = c.visibleItems.sorted { (c.measures[$0.id]?.size ?? 0) > (c.measures[$1.id]?.size ?? 0) }.prefix(4)
                    ForEach(Array(top)) { item in
                        HStack(spacing: FN.s1) {
                            Text(item.title).fnData()
                            Spacer()
                            FNPill(text: item.safety.shortTitle, state: item.safety.pill)
                            Text(Fmt.bytes(c.measures[item.id]?.size ?? 0)).fnData().frame(width: 80, alignment: .trailing)
                        }
                    }
                }
            } else {
                Text("See how much space caches, logs, developer files and old installers are using.")
                    .fnCaption().fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private var access: some View {
        FNSection(title: "Unreadable", caption: "\(app.derived.unreadable.count) folders weren't counted") {
            VStack(alignment: .leading, spacing: FN.s2) {
                if !app.fullDiskAccess {
                    Text("Give DiskWatch Full Disk Access in System Settings → Privacy & Security to include Mail, Messages, Safari and other protected app data.")
                        .fnCaption().fixedSize(horizontal: false, vertical: true)
                    Button("Open Privacy Settings") { FullDiskAccess.openSettings() }.buttonStyle(.fnSecondary)
                }
                ForEach(Array(app.derived.unreadable.prefix(6)), id: \.id) { n in
                    Text(Fmt.abbreviate(n.path)).font(FN.mono(11)).foregroundStyle(FN.muted).lineLimit(1).truncationMode(.middle)
                }
            }
        }
    }

    // MARK: Helpers

    private func fileLine(_ f: FileNode, detail: String) -> some View {
        Button { app.showInTree(f) } label: {
            HStack(spacing: FN.s1) {
                Image(systemName: FNIcon.symbol(for: f)).font(.system(size: 10)).foregroundStyle(FN.muted).frame(width: 14)
                VStack(alignment: .leading, spacing: 2) {
                    Text(f.name).fnData().lineLimit(1).truncationMode(.middle)
                    Text(Fmt.abbreviate(f.parent?.path ?? "")).font(FN.mono(10)).foregroundStyle(FN.muted).lineLimit(1).truncationMode(.middle)
                }
                Spacer()
                Text(detail).fnData()
            }
            .padding(.vertical, 7)
            .overlay(alignment: .bottom) { FNRule() }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .contextMenu { NodeMenuButtons(nodes: [f]) }
    }

    private func legend(_ c: Color, _ label: String, _ value: String) -> some View {
        HStack(spacing: 6) {
            Rectangle().fill(c).frame(width: 9, height: 9).fnBorder(FN.hair)
            Text(label).fnLabel()
            Text(value).fnData()
        }
    }
}

/// Stacked, flat composition bar — segments separated by 1px of the black ground.
struct CompositionBar: View {
    let segments: [(Color, Double)]
    var height: CGFloat = 18

    var body: some View {
        GeometryReader { g in
            HStack(spacing: 1) {
                ForEach(Array(segments.enumerated()), id: \.offset) { _, s in
                    if s.1 * g.size.width >= 1 {
                        Rectangle().fill(s.0).frame(width: max(1, s.1 * g.size.width - 1))
                    }
                }
                Spacer(minLength: 0)
            }
        }
        .frame(height: height)
        .background(FN.track)
        .fnBorder(FN.hair)
    }
}

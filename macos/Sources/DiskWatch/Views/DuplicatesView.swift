import SwiftUI

struct DuplicatesView: View {
    @Environment(AppState.self) private var app

    var body: some View {
        let model = app.duplicates
        VStack(spacing: 0) {
            ScrollView {
                LazyVStack(alignment: .leading, spacing: FN.s5) {
                    header(model)
                    if model.isRunning {
                        FNSection(title: nil) {
                            VStack(alignment: .leading, spacing: FN.s2) {
                                Text("Comparing").fnLabel()
                                FNGauge(fraction: model.progress, threshold: nil, height: 10, color: FN.accent)
                                Text(model.status).font(FN.mono(11)).foregroundStyle(FN.muted).lineLimit(1).truncationMode(.middle)
                                Button("Stop") { model.stop() }.buttonStyle(.fn(.danger, compact: true))
                            }
                        }
                    } else if model.hasRun && model.groups.isEmpty {
                        Text(model.status).fnCaption()
                    } else {
                        ForEach(model.groups.prefix(400)) { g in group(g, model) }
                        if model.groups.count > 400 {
                            Text("Showing the 400 groups that waste the most space.").fnCaption()
                        }
                    }
                }
                .padding(FN.s5)
            }
            if !model.groups.isEmpty && !model.isRunning {
                FNRule()
                footer(model)
            }
        }
        .background(FN.bg)
    }

    private func header(_ model: DuplicatesModel) -> some View {
        @Bindable var model = model
        return FNSection(title: "Duplicates",
                         caption: "Same-size files compared by SHA-256 — only byte-for-byte copies. Hard links are ignored.") {
            HStack(spacing: FN.s2) {
                FNMenuPicker(label: "Min size", selection: $model.minSize, options: [
                    (Int64(100_000), "100 KB"), (1_000_000, "1 MB"), (10_000_000, "10 MB"), (100_000_000, "100 MB")])
                Button { model.run(files: app.derived.files) } label: {
                    Label(model.hasRun ? "Search Again" : "Search", systemImage: "magnifyingglass")
                }
                .buttonStyle(.fnPrimary)
                .disabled(model.isRunning || app.root == nil)
            }
        } content: {
            if !model.groups.isEmpty {
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: FN.s3), count: 4), spacing: FN.s3) {
                    FNStatTile(label: "Reclaimable", value: Fmt.bytes(model.totalWasted), valueColor: FN.accent)
                    FNStatTile(label: "Groups", value: Fmt.count(model.groups.count))
                    FNStatTile(label: "Duplicate files", value: Fmt.count(model.groups.reduce(0) { $0 + $1.files.count - 1 }))
                    FNStatTile(label: "Marked", value: Fmt.bytes(model.markedSize))
                }
            }
        }
    }

    private func group(_ g: DuplicatesModel.Group, _ model: DuplicatesModel) -> some View {
        FNSection(title: nil, padding: 0) {
            VStack(alignment: .leading, spacing: 0) {
                HStack(spacing: FN.s1) {
                    Image(systemName: FNIcon.symbol(for: g.files[0])).font(.system(size: 10)).foregroundStyle(FN.muted)
                    Text(g.files[0].name).font(FN.mono(12, .semibold)).foregroundStyle(FN.fg).lineLimit(1).truncationMode(.middle)
                    Text("\(g.files.count) × \(Fmt.bytes(g.size))").font(FN.mono(11)).foregroundStyle(FN.muted)
                    Spacer()
                    FNPill(text: "\(Fmt.bytes(g.wasted)) wasted", state: .warn)
                }
                .padding(.horizontal, FN.s4).padding(.vertical, 10)
                .overlay(alignment: .bottom) { FNRule() }
                ForEach(g.files, id: \.id) { f in fileRow(f, model) }
            }
        }
    }

    private func fileRow(_ f: FileNode, _ model: DuplicatesModel) -> some View {
        let verdict = SafetyClassifier.classify(node: f)
        return HStack(spacing: FN.s2) {
            Toggle("", isOn: Binding(get: { model.marked.contains(f.id) }, set: { _ in model.toggle(f) }))
                .toggleStyle(.fnCheckbox).labelsHidden()
                .disabled(verdict.level == .protected)
            VStack(alignment: .leading, spacing: 2) {
                Text(Fmt.abbreviate(f.path)).font(FN.mono(11)).foregroundStyle(FN.fg).lineLimit(1).truncationMode(.middle)
                Text("Modified " + Fmt.date(f.modified)).font(FN.mono(10)).foregroundStyle(FN.muted)
            }
            Spacer()
            FNPill(text: verdict.level.shortTitle, state: verdict.level.pill)
            Button { RealUser.reveal([f.path]) } label: { Image(systemName: "arrow.up.right.square") }
                .buttonStyle(.fnGhost).help("Reveal in Finder")
            Button { RealUser.quickLook(f.path) } label: { Image(systemName: "eye") }
                .buttonStyle(.fnGhost).help("Quick Look")
        }
        .padding(.horizontal, FN.s4).padding(.vertical, 7)
        .background(model.marked.contains(f.id) ? FN.track : .clear)
        .overlay(alignment: .bottom) { FNRule() }
        .contextMenu { NodeMenuButtons(nodes: [f]) }
    }

    private func footer(_ model: DuplicatesModel) -> some View {
        HStack(spacing: FN.s2) {
            Menu {
                Button("Keep newest in each group") { model.autoMark(keep: .newest) }
                Button("Keep oldest in each group") { model.autoMark(keep: .oldest) }
                Button("Keep shortest path in each group") { model.autoMark(keep: .shortestPath) }
            } label: {
                Text("Auto-select").font(FN.mono(11, .semibold)).tracking(0.88).textCase(.uppercase).foregroundStyle(FN.fg)
                    .padding(.horizontal, 10).padding(.vertical, 6)
                    .fnBorder(FN.hair)
                    .contentShape(Rectangle())
            }
            .menuStyle(.button).buttonStyle(.plain).menuIndicator(.hidden).fixedSize()
            Button("Clear") { model.marked = [] }.buttonStyle(.fn(.secondary, compact: true))
            Text(model.status.hasPrefix("Keep") ? model.status : "APFS clones share storage — freed space can be less than shown.")
                .font(FN.mono(10)).foregroundStyle(model.status.hasPrefix("Keep") ? FN.warn : FN.muted)
                .lineLimit(1)
            Spacer()
            Text("\(model.marked.count) marked · \(Fmt.bytes(model.markedSize))").font(FN.mono(11, .semibold)).foregroundStyle(FN.fg)
            Button { app.requestDelete(model.markedNodes, permanent: false, source: "Duplicates") } label: {
                Label("Move to Trash…", systemImage: "trash")
            }
            .buttonStyle(.fnDanger)
            .disabled(model.marked.isEmpty)
        }
        .padding(.horizontal, FN.s5).padding(.vertical, FN.s2)
    }
}

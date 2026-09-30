import SwiftUI

struct CleanupView: View {
    @Environment(AppState.self) private var app
    @State private var permanent = false
    @State private var expanded: Set<String> = []
    @State private var snapshotToDelete: String?

    var body: some View {
        let model = app.cleanup
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: FN.s5) {
                    header(model)
                    if model.hasScanned {
                        ForEach(CleanupModel.Section.allCases) { section in
                            let items = model.visibleItems.filter { $0.section == section }
                            if !items.isEmpty {
                                FNSection(title: nil, padding: 0) {
                                    VStack(alignment: .leading, spacing: 0) {
                                        HStack {
                                            Text(section.rawValue).fnLabel(FN.fg)
                                            Spacer()
                                            let sum = items.reduce(Int64(0)) { $0 + (model.measures[$1.id]?.size ?? 0) }
                                            Text(Fmt.bytes(sum)).font(FN.mono(11)).foregroundStyle(FN.muted)
                                        }
                                        .padding(.horizontal, FN.s4).padding(.vertical, 10)
                                        .overlay(alignment: .bottom) { FNRule() }
                                        ForEach(items) { item in row(item, model) }
                                    }
                                }
                            }
                        }
                        if !model.snapshots.isEmpty { snapshots(model) }
                    }
                }
                .padding(FN.s5)
            }
            if model.hasScanned {
                FNRule()
                footer(model)
            }
        }
        .background(FN.bg)
        .confirmationDialog("Delete this Time Machine snapshot?", isPresented: Binding(get: { snapshotToDelete != nil }, set: { if !$0 { snapshotToDelete = nil } })) {
            Button("Delete Snapshot", role: .destructive) {
                if let s = snapshotToDelete { model.deleteSnapshot(s) }
            }
        } message: {
            Text("You won't be able to restore files from this point in time using the local snapshot. Backups on your Time Machine disk are not affected.")
        }
    }

    private func header(_ model: CleanupModel) -> some View {
        FNSection(title: "Cleanup", caption: model.hasScanned
                  ? (model.isMeasuring ? "Measuring \(model.measuring.count) locations…" : "\(model.visibleItems.count) locations with reclaimable space")
                  : "Checks well-known cache, log, developer and installer locations. Nothing is removed until you confirm.") {
            if model.hasScanned {
                Button { model.scanAll() } label: { Label("Re-analyze", systemImage: "arrow.clockwise") }
                    .buttonStyle(.fnSecondary)
                    .disabled(model.isMeasuring)
            }
        } content: {
            if model.hasScanned {
                LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: FN.s3), count: 4), spacing: FN.s3) {
                    FNStatTile(label: "Reclaimable", value: Fmt.bytes(model.totalFound), valueColor: FN.accent)
                    FNStatTile(label: "Selected", value: Fmt.bytes(model.selectedSize))
                    FNStatTile(label: "Safe items", value: Fmt.count(model.visibleItems.filter { $0.safety == .safe }.count))
                    FNStatTile(label: "Snapshots", value: Fmt.count(model.snapshots.count))
                }
            } else {
                Button { model.scanAll() } label: { Label("Analyze", systemImage: "sparkles") }
                    .buttonStyle(.fnPrimary)
            }
        }
    }

    @ViewBuilder
    private func row(_ item: CleanupModel.Item, _ model: CleanupModel) -> some View {
        let m = model.measures[item.id]
        let isOn = Binding(
            get: { model.checked.contains(item.id) },
            set: { if $0 { model.checked.insert(item.id) } else { model.checked.remove(item.id) } })
        VStack(alignment: .leading, spacing: FN.s1) {
            HStack(alignment: .top, spacing: FN.s2) {
                Toggle("", isOn: isOn).toggleStyle(.fnCheckbox).labelsHidden()
                    .disabled((m?.count ?? 0) == 0)
                    .padding(.top, 1)
                Image(systemName: item.symbol).font(.system(size: 12)).foregroundStyle(FN.muted).frame(width: 18)
                VStack(alignment: .leading, spacing: 4) {
                    HStack(spacing: FN.s1) {
                        Text(item.title).font(FN.mono(13, .semibold)).foregroundStyle(FN.fg)
                        FNPill(text: item.safety.shortTitle, state: item.safety.pill)
                        if item.permanentOnly { FNPill(text: "Permanent", state: .warn) }
                    }
                    Text(item.detail).font(FN.mono(11)).foregroundStyle(FN.muted).fixedSize(horizontal: false, vertical: true)
                }
                Spacer()
                VStack(alignment: .trailing, spacing: 2) {
                    if model.measuring.contains(item.id) {
                        Text("measuring…").font(FN.mono(11)).foregroundStyle(FN.muted)
                    } else if let m {
                        Text(Fmt.bytes(m.size)).font(FN.mono(14, .semibold)).foregroundStyle(m.size > 0 ? FN.fg : FN.muted)
                        Text("\(Fmt.count(m.count)) items").font(FN.mono(10)).foregroundStyle(FN.muted)
                    }
                }
                .frame(minWidth: 100, alignment: .trailing)
                Button {
                    if expanded.contains(item.id) { expanded.remove(item.id) } else { expanded.insert(item.id) }
                } label: {
                    Image(systemName: expanded.contains(item.id) ? "chevron.down" : "chevron.right")
                }
                .buttonStyle(.fnGhost)
                .disabled((m?.paths.isEmpty ?? true))
                .help("Show what's inside")
            }
            if expanded.contains(item.id), let m {
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(Array(m.paths.prefix(15).enumerated()), id: \.offset) { _, p in
                        HStack(spacing: FN.s1) {
                            Image(systemName: FNIcon.symbol(forPath: p.path)).font(.system(size: 9)).foregroundStyle(FN.muted).frame(width: 14)
                            Text(Fmt.abbreviate(p.path)).font(FN.mono(11)).foregroundStyle(FN.fg).lineLimit(1).truncationMode(.middle)
                            Spacer()
                            Text(Fmt.bytes(p.size)).font(FN.mono(11)).foregroundStyle(FN.muted)
                            Button { RealUser.reveal([p.path]) } label: { Image(systemName: "arrow.up.right.square") }
                                .buttonStyle(.fnGhost).help("Reveal in Finder")
                        }
                        .padding(.vertical, 5)
                        .overlay(alignment: .bottom) { FNRule() }
                    }
                    if m.paths.count > 15 {
                        Text("+ \(m.paths.count - 15) more").font(FN.mono(11)).foregroundStyle(FN.muted).padding(.top, 5)
                    }
                }
                .padding(.leading, 52)
            }
        }
        .padding(.horizontal, FN.s4).padding(.vertical, FN.s2)
        .overlay(alignment: .bottom) { FNRule() }
    }

    private func snapshots(_ model: CleanupModel) -> some View {
        FNSection(title: "Local snapshots", caption: "Time Machine keeps these hourly and frees them automatically when space runs low. Their space shows up as purgeable and isn't visible in a file scan.") {
            VStack(alignment: .leading, spacing: 0) {
                ForEach(model.snapshots, id: \.self) { s in
                    HStack {
                        Text(s).fnData()
                        Spacer()
                        Button("Delete…") { snapshotToDelete = s }.buttonStyle(.fn(.danger, compact: true))
                    }
                    .padding(.vertical, 6)
                    .overlay(alignment: .bottom) { FNRule() }
                }
                if let m = model.snapshotMessage { Text(m).fnCaption().padding(.top, FN.s1) }
            }
        }
    }

    private func footer(_ model: CleanupModel) -> some View {
        let ids = model.checked
        let permanentOnly = model.anyPermanentOnly(ids)
        return HStack(spacing: FN.s2) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Selected").fnLabel()
                Text(Fmt.bytes(model.selectedSize)).font(FN.mono(14, .semibold)).foregroundStyle(FN.fg)
            }
            Button("Select Safe") {
                model.checked = Set(model.visibleItems.filter { $0.safety == .safe && (model.measures[$0.id]?.count ?? 0) > 0 }.map(\.id))
            }
            .buttonStyle(.fn(.secondary, compact: true))
            Button("Clear") { model.checked = [] }.buttonStyle(.fn(.secondary, compact: true))
            Spacer()
            if !permanentOnly {
                FNSegmented(selection: $permanent, items: [(false, "Trash"), (true, "Permanent")])
            } else {
                Text("Includes Trash — deleted permanently").font(FN.mono(11)).foregroundStyle(FN.warn)
            }
            Button {
                let targets = model.targets(for: ids)
                app.requestDelete(paths: targets, permanent: permanent || permanentOnly, lockMode: permanentOnly, source: "Cleanup")
            } label: {
                Label("Clean Up…", systemImage: "trash")
            }
            .buttonStyle(.fnDanger)
            .disabled(ids.isEmpty || model.selectedSize == 0)
        }
        .padding(.horizontal, FN.s5).padding(.vertical, FN.s2)
    }
}

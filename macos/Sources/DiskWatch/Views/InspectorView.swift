import SwiftUI

struct InspectorView: View {
    @Environment(AppState.self) private var app

    var body: some View {
        ScrollView {
            Group {
                if app.selection.count > 1 {
                    multi(app.selection)
                } else if let n = app.focused {
                    single(n)
                } else {
                    Text("Select a file or folder to see details.").fnCaption()
                }
            }
            .padding(FN.s4)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .background(FN.bg)
    }

    private func single(_ n: FileNode) -> some View {
        let mode = app.sizeMode
        let verdict = SafetyClassifier.classify(node: n)
        let root = app.root
        return VStack(alignment: .leading, spacing: FN.s5) {
            VStack(alignment: .leading, spacing: FN.s1) {
                Text(n.isDirectory ? (n.isPackage ? "Package" : "Folder") : FileCategory.of(n).title).fnLabel()
                HStack(alignment: .firstTextBaseline, spacing: FN.s1) {
                    Image(systemName: FNIcon.symbol(for: n)).font(.system(size: 12)).foregroundStyle(FN.muted)
                    Text(n.displayName).font(FN.mono(14, .semibold)).foregroundStyle(FN.fg).lineLimit(3)
                }
            }

            VStack(alignment: .leading, spacing: 10) {
                Text(mode.title).fnLabel()
                Text(Fmt.bytes(n.size(mode))).fnValue()
                if let root, root !== n, root.size(mode) > 0 {
                    let f = Double(n.size(mode)) / Double(root.size(mode))
                    FNGauge(fraction: f, threshold: nil, height: 8, color: FN.accent)
                    Text("\(Fmt.percent(f)) of scan · \(Fmt.percent(n.fraction(ofParent: mode))) of parent").fnCaption()
                }
            }

            VStack(alignment: .leading, spacing: FN.s1) {
                HStack {
                    Text("Safety").fnLabel()
                    Spacer()
                    FNPill(text: verdict.level.title, state: verdict.level.pill)
                }
                ForEach(verdict.reasons, id: \.self) { r in
                    Text(r).font(FN.mono(11)).foregroundStyle(FN.muted).fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(FN.s2)
            .fnBorder(verdict.level == .caution ? FN.hair : verdict.level.color)

            section("Details") {
                row("Size on disk", Fmt.bytes(n.allocatedSize))
                row("File size", Fmt.bytes(n.logicalSize))
                if n.isDirectory {
                    row("Files", Fmt.count(n.fileCount))
                    row("Folders", Fmt.count(n.dirCount))
                }
                row("Modified", Fmt.date(n.modified))
                row("Created", Fmt.date(n.created))
                row("Last opened", Fmt.date(n.accessed))
                row("Owner", "\(Fmt.user(n.uid)):\(Fmt.group(n.gid))")
                row("Mode", Fmt.permissions(n.mode))
                if n.unreadable { row("Status", "Not readable", FN.warn) }
                if n.excluded { row("Status", "Skipped", FN.muted) }
                if n.hardlinkDuplicate { row("Status", "Hard link", FN.muted) }
            }

            section("Location") {
                Text(n.path).font(FN.mono(11)).foregroundStyle(FN.fg).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
            }

            if n.isDirectory && !n.children.isEmpty {
                section("Largest inside") {
                    let top = n.children.sorted { $0.size(mode) > $1.size(mode) }.prefix(8)
                    let maxSize = Double(max(1, top.first?.size(mode) ?? 1))
                    ForEach(Array(top), id: \.id) { c in
                        Button { app.select(c) } label: {
                            VStack(alignment: .leading, spacing: 4) {
                                HStack {
                                    Text(c.name).font(FN.mono(11)).foregroundStyle(FN.fg).lineLimit(1).truncationMode(.middle)
                                    Spacer()
                                    Text(Fmt.bytes(c.size(mode))).font(FN.mono(11)).foregroundStyle(FN.muted)
                                }
                                FNBar(fraction: Double(c.size(mode)) / maxSize, height: 3)
                            }
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                    }
                }
            }

            actions([n], verdict: verdict)
        }
    }

    private func multi(_ nodes: [FileNode]) -> some View {
        let mode = app.sizeMode
        let total = nodes.filter { n in !nodes.contains { n.isDescendant(of: $0) } }.reduce(Int64(0)) { $0 + $1.size(mode) }
        let levels = nodes.map { SafetyClassifier.classify(node: $0).level }
        let worst = levels.max() ?? .safe
        return VStack(alignment: .leading, spacing: FN.s5) {
            VStack(alignment: .leading, spacing: FN.s1) {
                Text("\(nodes.count) items selected").fnLabel()
                Text(Fmt.bytes(total)).fnValue()
            }
            section("Safety") {
                ForEach(SafetyLevel.allCases.reversed()) { lvl in
                    let c = levels.filter { $0 == lvl }.count
                    if c > 0 {
                        HStack {
                            FNPill(text: lvl.shortTitle, state: lvl.pill)
                            Spacer()
                            Text("\(c)").fnData()
                        }
                    }
                }
            }
            actions(nodes, verdict: SafetyVerdict(level: worst, reasons: []))
        }
    }

    private func actions(_ nodes: [FileNode], verdict: SafetyVerdict) -> some View {
        let blocked = verdict.level == .protected && nodes.count == 1
        return VStack(alignment: .leading, spacing: FN.s1) {
            Text("Actions").fnLabel()
            HStack(spacing: FN.s1) {
                Button { RealUser.reveal(nodes.map(\.path)) } label: { Text("Reveal").frame(maxWidth: .infinity) }
                    .buttonStyle(.fn(.secondary, compact: true))
                if nodes.count == 1 {
                    Button { RealUser.quickLook(nodes[0].path) } label: { Text("Preview").frame(maxWidth: .infinity) }
                        .buttonStyle(.fn(.secondary, compact: true))
                }
            }
            if nodes.count == 1, nodes[0].isDirectory {
                HStack(spacing: FN.s1) {
                    Button { app.rescan(nodes[0]) } label: { Text("Rescan").frame(maxWidth: .infinity) }
                        .buttonStyle(.fn(.secondary, compact: true))
                    Button { app.showInTreemap(nodes[0]) } label: { Text("Treemap").frame(maxWidth: .infinity) }
                        .buttonStyle(.fn(.secondary, compact: true))
                }
            }
            Button { app.requestDelete(nodes, permanent: false) } label: {
                Label("Move to Trash…", systemImage: "trash").frame(maxWidth: .infinity)
            }
            .buttonStyle(.fnDanger)
            .disabled(blocked)
            .padding(.top, FN.s1)
            Button { app.requestDelete(nodes, permanent: true) } label: {
                Label("Delete Permanently…", systemImage: "xmark").frame(maxWidth: .infinity)
            }
            .buttonStyle(.fnDanger)
            .disabled(blocked)
            if blocked {
                Text("DiskWatch won't remove protected items.").fnCaption()
            }
        }
    }

    private func section<C: View>(_ title: String, @ViewBuilder _ content: () -> C) -> some View {
        VStack(alignment: .leading, spacing: FN.s1) {
            Text(title).fnLabel()
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.bottom, 4)
                .overlay(alignment: .bottom) { FNRule() }
            content()
        }
    }

    private func row(_ k: String, _ v: String, _ color: Color = FN.fg) -> some View {
        HStack(alignment: .firstTextBaseline) {
            Text(k).font(FN.mono(11)).foregroundStyle(FN.muted)
            Spacer()
            Text(v).font(FN.mono(11)).foregroundStyle(color).multilineTextAlignment(.trailing).textSelection(.enabled)
        }
    }
}

import SwiftUI

struct FilesView: View {
    @Environment(AppState.self) private var app
    @State private var results: [FileNode] = []
    @State private var matchCount = 0
    @State private var matchSize: Int64 = 0
    @State private var sortKey: SortKey = .size
    @State private var ascending = false
    @State private var selection = Set<FileNode.ID>()
    @State private var anchor: FileNode.ID?
    @State private var computing = false

    enum SortKey { case name, size, modified, accessed, kind }

    private struct Key: Equatable {
        let filter: FilesFilter
        let count: Int
        let version: Int
        let mode: SizeMode
    }

    var body: some View {
        VStack(spacing: 0) {
            filterBar
            FNRule()
            ScrollView {
                LazyVStack(spacing: 0, pinnedViews: [.sectionHeaders]) {
                    Section {
                        ForEach(results, id: \.id) { n in
                            FileRow(node: n, mode: app.sizeMode)
                                .modifier(FNRowBackground(selected: selection.contains(n.id)))
                                .contentShape(Rectangle())
                                .onTapGesture { click(n) }
                                .simultaneousGesture(TapGesture(count: 2).onEnded { RealUser.quickLook(n.path) })
                                .contextMenu { NodeMenuButtons(nodes: menuNodes(for: n)) }
                        }
                    } header: {
                        header
                    }
                }
            }
            .background(FN.bg)
            FNRule()
            footer
        }
        .background(FN.bg)
        .task(id: Key(filter: app.filesFilter, count: app.derived.files.count, version: app.treeVersion, mode: app.sizeMode)) {
            await recompute()
        }
        .onChange(of: sortKey) { resort() }
        .onChange(of: ascending) { resort() }
    }

    private func click(_ n: FileNode) {
        FNListSelection.click(n.id, order: results.map(\.id), selection: &selection, anchor: &anchor)
        app.selection = results.filter { selection.contains($0.id) }
    }

    private func menuNodes(for n: FileNode) -> [FileNode] {
        selection.contains(n.id) ? results.filter { selection.contains($0.id) } : [n]
    }

    private func sortBy(_ k: SortKey) {
        if sortKey == k { ascending.toggle() } else { sortKey = k; ascending = k == .name || k == .kind }
    }

    private func resort() { results = Self.sorted(results, key: sortKey, ascending: ascending, mode: app.sizeMode) }

    static func sorted(_ items: [FileNode], key: SortKey, ascending: Bool, mode: SizeMode) -> [FileNode] {
        func cmp<T: Comparable>(_ a: T, _ b: T) -> Bool { ascending ? a < b : a > b }
        switch key {
        case .name: return items.sorted { cmp($0.name.lowercased(), $1.name.lowercased()) }
        case .size: return items.sorted { cmp($0.size(mode), $1.size(mode)) }
        case .modified: return items.sorted { cmp($0.modified, $1.modified) }
        case .accessed: return items.sorted { cmp($0.accessed, $1.accessed) }
        case .kind: return items.sorted { cmp(FileCategory.of($0).title, FileCategory.of($1).title) }
        }
    }

    private func recompute() async {
        computing = true
        let files = app.derived.files
        let f = app.filesFilter
        let mode = app.sizeMode
        let key = sortKey, asc = ascending
        let (res, count, total) = await Task.detached(priority: .userInitiated) { () -> ([FileNode], Int, Int64) in
            let needle = f.text.trimmingCharacters(in: .whitespaces)
            let cutoff = f.olderThanDays > 0 ? Date().timeIntervalSince1970 - Double(f.olderThanDays) * 86400 : 0
            var out: [FileNode] = []
            var count = 0
            var total: Int64 = 0
            for n in files {
                if n.parent == nil && files.count > 1 { continue }
                if n.size(mode) < f.minSize { continue }
                if cutoff > 0 && n.modified >= cutoff { continue }
                if let e = f.ext, n.nameExtension != e && !(e == "(none)" && n.nameExtension.isEmpty) { continue }
                if let c = f.category, FileCategory.of(n) != c { continue }
                if !needle.isEmpty && n.name.range(of: needle, options: [.caseInsensitive, .diacriticInsensitive]) == nil { continue }
                count += 1
                total += n.size(mode)
                if out.count < f.limit { out.append(n) }
            }
            return (FilesView.sorted(out, key: key, ascending: asc, mode: mode), count, total)
        }.value
        results = res
        matchCount = count
        matchSize = total
        let alive = Set(res.map(\.id))
        selection = selection.intersection(alive)
        computing = false
    }

    // MARK: Chrome

    private var header: some View {
        HStack(spacing: FN.s2) {
            FNColumnHeader(title: "Name", active: sortKey == .name, ascending: ascending) { sortBy(.name) }
                .frame(minWidth: 150, maxWidth: .infinity)
            FNColumnHeader(title: app.sizeMode.title, active: sortKey == .size, ascending: ascending, alignment: .trailing) { sortBy(.size) }
                .frame(width: FileRow.sizeW)
            FNColumnHeader(title: "Modified", active: sortKey == .modified, ascending: ascending) { sortBy(.modified) }
                .frame(width: FileRow.dateW)
            FNColumnHeader(title: "Kind", active: sortKey == .kind, ascending: ascending) { sortBy(.kind) }
                .frame(width: FileRow.kindW)
            FNColumnHeader(title: "Safety").frame(width: FileRow.safetyW)
            FNColumnHeader(title: "Location").frame(minWidth: 100, maxWidth: .infinity)
        }
        .padding(.horizontal, FN.s5).padding(.vertical, 9)
        .background(FN.bg)
        .overlay(alignment: .bottom) { FNRule() }
    }

    private var filterBar: some View {
        @Bindable var app = app
        return HStack(spacing: FN.s2) {
            FNTextField(placeholder: "Name contains…", text: $app.filesFilter.text, width: 180)
            FNMenuPicker(label: "Kind", selection: $app.filesFilter.category,
                         options: [(FileCategory?.none, "All kinds")] + FileCategory.allCases.map { (Optional($0), $0.title) })
            FNMenuPicker(label: "Min", selection: $app.filesFilter.minSize, options: [
                (Int64(0), "Any size"), (1_000_000, "1 MB"), (10_000_000, "10 MB"), (100_000_000, "100 MB"),
                (500_000_000, "500 MB"), (1_000_000_000, "1 GB"), (5_000_000_000, "5 GB")])
            FNMenuPicker(label: "Untouched", selection: $app.filesFilter.olderThanDays, options: [
                (0, "Any time"), (30, "30 days"), (90, "90 days"), (180, "6 months"), (365, "1 year"), (730, "2 years"), (1825, "5 years")])
            if let e = app.filesFilter.ext {
                Button { app.filesFilter.ext = nil } label: { Label(".\(e)", systemImage: "xmark") }
                    .buttonStyle(.fn(.primary, compact: true))
            }
            Spacer()
            Button("Reset") { app.filesFilter = FilesFilter() }
                .buttonStyle(.fn(.secondary, compact: true))
                .disabled(app.filesFilter == FilesFilter())
        }
        .padding(.horizontal, FN.s5).padding(.vertical, FN.s2)
    }

    private var footer: some View {
        @Bindable var app = app
        let selected = results.filter { selection.contains($0.id) }
        let selSize = selected.reduce(Int64(0)) { $0 + $1.size(app.sizeMode) }
        return HStack(spacing: FN.s2) {
            Text("\(Fmt.count(matchCount)) files · \(Fmt.bytes(matchSize))" + (matchCount > results.count ? " · largest \(Fmt.count(results.count)) shown" : "") + (computing ? " · filtering…" : ""))
                .font(FN.mono(11)).foregroundStyle(FN.muted)
            FNMenuPicker(selection: $app.filesFilter.limit, options: [(500, "Show 500"), (2000, "Show 2,000"), (10000, "Show 10,000")])
            Spacer()
            if !selected.isEmpty {
                Text("\(selected.count) selected · \(Fmt.bytes(selSize))").font(FN.mono(11, .semibold)).foregroundStyle(FN.fg)
                Button("Reveal") { RealUser.reveal(selected.map(\.path)) }.buttonStyle(.fn(.secondary, compact: true))
                Button("Move to Trash…") { app.requestDelete(selected, permanent: false) }.buttonStyle(.fn(.danger, compact: true))
                Button("Delete…") { app.requestDelete(selected, permanent: true) }.buttonStyle(.fn(.danger, compact: true))
            }
        }
        .padding(.horizontal, FN.s5).padding(.vertical, FN.s1)
    }
}

struct FileRow: View {
    static let sizeW: CGFloat = 86, dateW: CGFloat = 78, kindW: CGFloat = 130, safetyW: CGFloat = 86
    let node: FileNode
    let mode: SizeMode
    @Environment(\.fnSelected) private var selected

    var body: some View {
        let ink = selected ? FN.bg : FN.fg
        let sub = selected ? FN.bg : FN.muted
        let level = SafetyClassifier.classify(node: node).level
        let cat = FileCategory.of(node)
        HStack(spacing: FN.s2) {
            HStack(spacing: FN.s1) {
                Image(systemName: FNIcon.symbol(for: node)).font(.system(size: 10)).foregroundStyle(sub).frame(width: 14)
                Text(node.name).lineLimit(1).truncationMode(.middle).foregroundStyle(ink)
            }
            .frame(minWidth: 150, maxWidth: .infinity, alignment: .leading)
            .help(node.path)
            Text(Fmt.bytes(node.size(mode))).fontWeight(.semibold).foregroundStyle(ink)
                .frame(width: Self.sizeW, alignment: .trailing)
            Text(Fmt.shortDate(node.modified)).foregroundStyle(sub).frame(width: Self.dateW, alignment: .leading)
                .help(Fmt.date(node.modified))
            HStack(spacing: 6) {
                Rectangle().fill(cat.color).frame(width: 8, height: 8).fnBorder(selected ? FN.bg : .clear)
                Text(cat.title).lineLimit(1).foregroundStyle(sub)
            }
            .frame(width: Self.kindW, alignment: .leading)
            FNPill(text: level.shortTitle, state: selected ? .inverse : level.pill)
            .frame(width: Self.safetyW, alignment: .leading)
            Text(Fmt.abbreviate(node.parent?.path ?? "")).lineLimit(1).truncationMode(.middle).foregroundStyle(sub)
                .frame(minWidth: 100, maxWidth: .infinity, alignment: .leading)
        }
        .font(FN.mono(12))
        .padding(.horizontal, FN.s5)
        .frame(height: 28)
    }
}

/// SwiftUI version of the shared item actions.
struct NodeMenuButtons: View {
    @Environment(AppState.self) private var app
    let nodes: [FileNode]

    var body: some View {
        let first = nodes[0]
        Button("Reveal in Finder") { RealUser.reveal(nodes.map(\.path)) }
        if nodes.count == 1 {
            Button("Open") { RealUser.open(first.path) }
            Button("Quick Look") { RealUser.quickLook(first.path) }
        }
        Button(nodes.count == 1 ? "Copy Path" : "Copy Paths") {
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(nodes.map(\.path).joined(separator: "\n"), forType: .string)
        }
        if nodes.count == 1 {
            Divider()
            Button("Show in Tree") { app.showInTree(first) }
            Button("Show in Treemap") { app.showInTreemap(first) }
        }
        Divider()
        Button("Move to Trash…") { app.requestDelete(nodes, permanent: false) }
        Button("Delete Permanently…") { app.requestDelete(nodes, permanent: true) }
    }
}

/// Safety badge in the system's pill form.
struct SafetyBadge: View {
    let level: SafetyLevel
    var compact = false

    var body: some View {
        FNPill(text: compact ? level.shortTitle : level.title, state: level.pill)
            .help(level.explanation)
    }
}

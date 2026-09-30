import SwiftUI

struct TypesView: View {
    @Environment(AppState.self) private var app
    @State private var sortKey: SortKey = .total
    @State private var ascending = false
    @State private var hovered: FileCategory?

    enum SortKey { case ext, files, total, average }

    var body: some View {
        let mode = app.sizeMode
        let cats = app.derived.categories
        let total = max(1, cats.reduce(Int64(0)) { $0 + $1.size(mode) })
        ScrollView {
            VStack(alignment: .leading, spacing: FN.s5) {
                FNSection(title: "By kind", caption: "\(Fmt.count(app.derived.files.count)) files · \(Fmt.bytes(total)) · click a kind to list its files") {
                    VStack(alignment: .leading, spacing: FN.s3) {
                        CompositionBar(segments: cats.map { c in
                            (hovered == nil || hovered == c.category ? c.category.color : c.category.color.opacity(0.25),
                             Double(c.size(mode)) / Double(total))
                        }, height: 28)
                        VStack(spacing: 0) {
                            ForEach(cats) { c in
                                let frac = Double(c.size(mode)) / Double(total)
                                Button {
                                    app.filesFilter = FilesFilter(category: c.category)
                                    app.tab = .files
                                } label: {
                                    HStack(spacing: FN.s2) {
                                        Rectangle().fill(c.category.color).frame(width: 10, height: 10)
                                        Image(systemName: c.category.symbol).font(.system(size: 10)).foregroundStyle(FN.muted).frame(width: 16)
                                        Text(c.category.title).fnData().lineLimit(1).frame(width: 170, alignment: .leading)
                                        FNBar(fraction: frac, color: c.category.color, height: 6)
                                        Text(Fmt.bytes(c.size(mode))).font(FN.mono(12, .semibold)).foregroundStyle(FN.fg).frame(width: 90, alignment: .trailing)
                                        Text(Fmt.percent(frac)).font(FN.mono(11)).foregroundStyle(FN.muted).frame(width: 54, alignment: .trailing)
                                        Text("\(Fmt.count(c.count)) files").font(FN.mono(11)).foregroundStyle(FN.muted).lineLimit(1).frame(width: 96, alignment: .trailing)
                                    }
                                    .padding(.vertical, 8)
                                    .background(hovered == c.category ? FN.track : .clear)
                                    .overlay(alignment: .bottom) { FNRule() }
                                    .contentShape(Rectangle())
                                }
                                .buttonStyle(.plain)
                                .onHover { hovered = $0 ? c.category : (hovered == c.category ? nil : hovered) }
                            }
                        }
                    }
                }

                FNSection(title: nil, padding: 0) {
                    VStack(spacing: 0) {
                        FNSectionHeading(title: "By extension", caption: "Top 500 · click a row to list those files")
                            .padding(FN.s6)
                        HStack(spacing: FN.s2) {
                            FNColumnHeader(title: "Extension", active: sortKey == .ext, ascending: ascending) { sortBy(.ext) }
                                .frame(minWidth: 110, maxWidth: .infinity)
                            FNColumnHeader(title: "Kind").frame(width: 150)
                            FNColumnHeader(title: "Files", active: sortKey == .files, ascending: ascending, alignment: .trailing) { sortBy(.files) }
                                .frame(width: 70)
                            FNColumnHeader(title: "Total", active: sortKey == .total, ascending: ascending, alignment: .trailing) { sortBy(.total) }
                                .frame(width: 86)
                            FNColumnHeader(title: "Average", active: sortKey == .average, ascending: ascending, alignment: .trailing) { sortBy(.average) }
                                .frame(width: 80)
                            FNColumnHeader(title: "Share", alignment: .trailing).frame(width: 56)
                        }
                        .padding(.horizontal, FN.s4).padding(.vertical, 9)
                        .overlay(alignment: .bottom) { FNRule() }
                        LazyVStack(spacing: 0) {
                            ForEach(sortedExtensions) { e in
                                Button { showExt(e.ext) } label: { ExtRow(e: e, mode: mode, total: total) }
                                    .buttonStyle(.plain)
                            }
                        }
                    }
                }
            }
            .padding(FN.s5)
        }
        .background(FN.bg)
    }

    private func sortBy(_ k: SortKey) {
        if sortKey == k { ascending.toggle() } else { sortKey = k; ascending = k == .ext }
    }

    private var sortedExtensions: [ExtensionStat] {
        let mode = app.sizeMode
        let items = Array(app.derived.extensions.prefix(500))
        func cmp<T: Comparable>(_ a: T, _ b: T) -> Bool { ascending ? a < b : a > b }
        switch sortKey {
        case .ext: return items.sorted { cmp($0.ext, $1.ext) }
        case .files: return items.sorted { cmp($0.count, $1.count) }
        case .total: return items.sorted { cmp($0.size(mode), $1.size(mode)) }
        case .average: return items.sorted { cmp($0.size(mode) / Int64(max(1, $0.count)), $1.size(mode) / Int64(max(1, $1.count))) }
        }
    }

    private func showExt(_ ext: String) {
        app.filesFilter = FilesFilter(ext: ext)
        app.tab = .files
    }
}

private struct ExtRow: View {
    let e: ExtensionStat
    let mode: SizeMode
    let total: Int64
    @State private var hover = false

    var body: some View {
        HStack(spacing: FN.s2) {
            HStack(spacing: FN.s1) {
                Rectangle().fill(e.category.color).frame(width: 8, height: 8)
                Text(e.ext == "(none)" ? "(no extension)" : "." + e.ext).foregroundStyle(FN.fg)
            }
            .frame(minWidth: 110, maxWidth: .infinity, alignment: .leading)
            Text(e.category.title).foregroundStyle(FN.muted).lineLimit(1).frame(width: 150, alignment: .leading)
            Text(Fmt.count(e.count)).foregroundStyle(FN.muted).frame(width: 70, alignment: .trailing)
            Text(Fmt.bytes(e.size(mode))).fontWeight(.semibold).foregroundStyle(FN.fg).frame(width: 86, alignment: .trailing)
            Text(Fmt.bytes(e.count > 0 ? e.size(mode) / Int64(e.count) : 0)).foregroundStyle(FN.muted).frame(width: 80, alignment: .trailing)
            Text(Fmt.percent(Double(e.size(mode)) / Double(total))).foregroundStyle(FN.muted).frame(width: 56, alignment: .trailing)
        }
        .font(FN.mono(12))
        .padding(.horizontal, FN.s4)
        .frame(height: 28)
        .background(hover ? FN.track : .clear)
        .overlay(alignment: .bottom) { FNRule() }
        .contentShape(Rectangle())
        .onHover { hover = $0 }
    }
}

import SwiftUI

struct TreemapTile {
    let node: FileNode
    let rect: CGRect
    let depth: Int
    let isLeaf: Bool
}

enum TreemapLayout {
    static let maxTiles = 25_000

    static func build(root: FileNode, size: CGSize, mode: SizeMode) -> [TreemapTile] {
        var out: [TreemapTile] = []
        out.reserveCapacity(4096)
        layout(children: root, in: CGRect(origin: .zero, size: size).insetBy(dx: 1, dy: 1), depth: 0, mode: mode, out: &out)
        return out
    }

    private static func layout(children node: FileNode, in rect: CGRect, depth: Int, mode: SizeMode, out: inout [TreemapTile]) {
        guard rect.width >= 2, rect.height >= 2 else { return }
        let all = node.children.filter { $0.size(mode) > 0 }.sorted { $0.size(mode) > $1.size(mode) }
        let total = Double(all.reduce(Int64(0)) { $0 + $1.size(mode) })
        guard total > 0 else { return }
        let scale = Double(rect.width * rect.height) / total
        var items: [(FileNode, Double)] = []
        for c in all {
            let a = Double(c.size(mode)) * scale
            if a < 3 { break }
            items.append((c, a))
        }
        for (child, r) in squarify(items, in: rect) {
            if out.count >= maxTiles { return }
            let canNest = child.isDirectory && !child.children.isEmpty && r.width > 14 && r.height > 14
            if canNest {
                out.append(TreemapTile(node: child, rect: r, depth: depth, isLeaf: false))
                let header: CGFloat = (r.width > 70 && r.height > 40) ? 15 : 0
                let inner = CGRect(x: r.minX + 2, y: r.minY + header + 2, width: r.width - 4, height: r.height - header - 4)
                layout(children: child, in: inner, depth: depth + 1, mode: mode, out: &out)
            } else {
                out.append(TreemapTile(node: child, rect: r, depth: depth, isLeaf: true))
            }
        }
    }

    /// Squarified treemap (Bruls, Huizing, van Wijk). `items` must be sorted largest first.
    static func squarify(_ items: [(FileNode, Double)], in start: CGRect) -> [(FileNode, CGRect)] {
        var out: [(FileNode, CGRect)] = []
        var rect = start
        var i = 0
        func worst(_ sum: Double, _ big: Double, _ small: Double, _ w: Double) -> Double {
            let s2 = sum * sum, w2 = w * w
            return max(w2 * big / s2, s2 / (w2 * small))
        }
        while i < items.count {
            let w = Double(min(rect.width, rect.height))
            guard w > 0.5 else { break }
            var sum = items[i].1
            let big = items[i].1
            var best = worst(sum, big, items[i].1, w)
            var end = i + 1
            while end < items.count {
                let ns = sum + items[end].1
                let nw = worst(ns, big, items[end].1, w)
                if nw > best { break }
                sum = ns; best = nw; end += 1
            }
            if rect.width >= rect.height {
                let colW = CGFloat(sum) / rect.height
                var y = rect.minY
                for k in i..<end {
                    let h = CGFloat(items[k].1) / colW
                    out.append((items[k].0, CGRect(x: rect.minX, y: y, width: colW, height: h)))
                    y += h
                }
                rect = CGRect(x: rect.minX + colW, y: rect.minY, width: max(0, rect.width - colW), height: rect.height)
            } else {
                let rowH = CGFloat(sum) / rect.width
                var x = rect.minX
                for k in i..<end {
                    let wd = CGFloat(items[k].1) / rowH
                    out.append((items[k].0, CGRect(x: x, y: rect.minY, width: wd, height: rowH)))
                    x += wd
                }
                rect = CGRect(x: rect.minX, y: rect.minY + rowH, width: rect.width, height: max(0, rect.height - rowH))
            }
            i = end
        }
        return out
    }
}

final class TreemapCache {
    var key = ""
    var tiles: [TreemapTile] = []

    func tiles(root: FileNode, size: CGSize, mode: SizeMode, version: Int) -> [TreemapTile] {
        let k = "\(ObjectIdentifier(root).hashValue)|\(Int(size.width))x\(Int(size.height))|\(mode.rawValue)|\(version)"
        if k != key {
            key = k
            tiles = TreemapLayout.build(root: root, size: size, mode: mode)
        }
        return tiles
    }
}

struct TreemapView: View {
    @Environment(AppState.self) private var app
    @State private var cache = TreemapCache()
    @State private var hovered: FileNode?
    @State private var hoverPoint: CGPoint = .zero

    var body: some View {
        if let root = app.root {
            let current = (app.treemapRoot.flatMap { $0.isDescendant(of: root) || $0 === root ? $0 : nil }) ?? root
            VStack(spacing: 0) {
                breadcrumb(current: current, root: root)
                FNRule()
                GeometryReader { geo in
                    let tiles = cache.tiles(root: current, size: geo.size, mode: app.sizeMode, version: app.treeVersion)
                    ZStack(alignment: .topLeading) {
                        canvas(tiles: tiles)
                            .onContinuousHover { phase in
                                switch phase {
                                case .active(let p):
                                    hoverPoint = p
                                    let hit = tile(at: p, in: tiles)?.node
                                    if hit !== hovered { hovered = hit }
                                case .ended:
                                    hovered = nil
                                }
                            }
                            .onTapGesture(count: 2, coordinateSpace: .local) { p in
                                guard let t = tile(at: p, in: tiles) else { return }
                                let target = t.node.isDirectory ? t.node : t.node.parent
                                if let target, !target.children.isEmpty { app.treemapRoot = target }
                            }
                            .onTapGesture(count: 1, coordinateSpace: .local) { p in
                                if let t = tile(at: p, in: tiles) { app.select(t.node, reveal: true) }
                            }
                            .contextMenu { contextMenu(for: hovered, current: current) }
                        if let h = hovered { tooltip(h, in: geo.size) }
                        if tiles.isEmpty {
                            Text("Nothing to show here.").fnCaption()
                                .frame(maxWidth: .infinity, maxHeight: .infinity)
                        }
                    }
                }
                .padding(FN.s2)
                FNRule()
                legend
            }
            .background(FN.bg)
        }
    }

    /// Single-line label trimmed to roughly fit `width` (Canvas text would otherwise wrap and overlap).
    static func fit(_ s: String, _ width: CGFloat, _ charWidth: CGFloat) -> String {
        let maxChars = Int(width / charWidth)
        guard s.count > maxChars else { return s }
        guard maxChars > 2 else { return "" }
        return String(s.prefix(maxChars - 1)) + "…"
    }

    private func tile(at p: CGPoint, in tiles: [TreemapTile]) -> TreemapTile? {
        // Later tiles are drawn on top (deeper), so search backwards.
        for t in tiles.reversed() where t.rect.contains(p) { return t }
        return nil
    }

    /// Flat fills only: kinds on the lightness ramp, folders as bordered surface compartments.
    private func canvas(tiles: [TreemapTile]) -> some View {
        let selected = Set(app.selection)
        let hov = hovered
        let mode = app.sizeMode
        return Canvas(rendersAsynchronously: false) { ctx, _ in
            ctx.fill(Path(CGRect(origin: .zero, size: ctx.clipBoundingRect.size)), with: .color(FN.bg))
            for t in tiles {
                let r = t.rect
                if t.isLeaf {
                    let inner = r.insetBy(dx: 0.5, dy: 0.5)
                    if t.node.isDirectory {
                        ctx.fill(Path(inner), with: .color(FN.track))
                        ctx.stroke(Path(inner.insetBy(dx: 0.5, dy: 0.5)), with: .color(FN.hair), lineWidth: 1)
                    } else {
                        ctx.fill(Path(inner), with: .color(FileCategory.of(t.node).color))
                    }
                    if r.width > 54 && r.height > 18 {
                        let ink = t.node.isDirectory ? FN.fg : FileCategory.of(t.node).onFill
                        ctx.draw(Text(Self.fit(t.node.name, r.width - 8, 6.4)).font(FN.mono(10, .semibold)).foregroundStyle(ink),
                                 at: CGPoint(x: r.minX + 4, y: r.minY + 3), anchor: .topLeading)
                        if r.height > 34 {
                            ctx.draw(Text(Fmt.bytes(t.node.size(mode))).font(FN.mono(9)).foregroundStyle(ink.opacity(0.75)),
                                     at: CGPoint(x: r.minX + 4, y: r.minY + 17), anchor: .topLeading)
                        }
                    }
                } else {
                    ctx.fill(Path(r), with: .color(FN.surface))
                    ctx.stroke(Path(r.insetBy(dx: 0.5, dy: 0.5)), with: .color(FN.border), lineWidth: 1)
                    if r.width > 70 && r.height > 40 {
                        let label = "\(t.node.name.uppercased())  \(Fmt.bytes(t.node.size(mode)))"
                        ctx.draw(Text(Self.fit(label, r.width - 10, 7.2)).font(FN.mono(10, .semibold)).tracking(0.6).foregroundStyle(FN.muted),
                                 at: CGPoint(x: r.minX + 5, y: r.minY + 2), anchor: .topLeading)
                    }
                }
            }
            if let hov, let t = tiles.last(where: { $0.node === hov }) {
                ctx.stroke(Path(t.rect.insetBy(dx: 0.5, dy: 0.5)), with: .color(FN.fg), lineWidth: 1)
            }
            for t in tiles where selected.contains(t.node) {
                ctx.stroke(Path(t.rect.insetBy(dx: 1, dy: 1)), with: .color(FN.accentBright), lineWidth: 2)
            }
        }
    }

    @ViewBuilder
    private func tooltip(_ n: FileNode, in size: CGSize) -> some View {
        let w: CGFloat = 300
        let x = min(max(8, hoverPoint.x + 16), size.width - w - 8)
        let y = min(hoverPoint.y + 16, size.height - 110)
        VStack(alignment: .leading, spacing: 6) {
            Text(n.isDirectory ? "Folder" : FileCategory.of(n).title).fnLabel()
            Text(n.name).font(FN.mono(12, .semibold)).foregroundStyle(FN.fg).lineLimit(1)
            HStack(spacing: FN.s1) {
                Text(Fmt.bytes(n.size(app.sizeMode))).fnData(FN.accent)
                if n.parent != nil { Text(Fmt.percent(n.fraction(ofParent: app.sizeMode)) + " of parent").font(FN.mono(11)).foregroundStyle(FN.muted) }
            }
            if n.isDirectory {
                Text("\(Fmt.count(n.fileCount)) files · \(Fmt.count(n.dirCount)) folders").font(FN.mono(11)).foregroundStyle(FN.muted)
            } else {
                Text("Modified " + Fmt.shortDate(n.modified)).font(FN.mono(11)).foregroundStyle(FN.muted)
            }
            Text(Fmt.abbreviate(n.path)).font(FN.mono(10)).foregroundStyle(FN.muted).lineLimit(2).truncationMode(.middle)
        }
        .padding(FN.s2)
        .frame(width: w, alignment: .leading)
        .background(FN.bg)
        .fnBorder(FN.hair)
        .offset(x: x, y: max(8, y))
        .allowsHitTesting(false)
    }

    @ViewBuilder
    private func contextMenu(for n: FileNode?, current: FileNode) -> some View {
        if let n {
            Text(n.name)
            Button("Reveal in Finder") { RealUser.reveal([n.path]) }
            Button("Quick Look") { RealUser.quickLook(n.path) }
            if n.isDirectory && !n.children.isEmpty { Button("Zoom In") { app.treemapRoot = n } }
            if current.parent != nil { Button("Zoom Out") { app.treemapRoot = current.parent } }
            Button("Show in Tree") { app.showInTree(n) }
            Divider()
            Button("Move to Trash…") { app.requestDelete([n], permanent: false) }
            Button("Delete Permanently…") { app.requestDelete([n], permanent: true) }
        }
    }

    private func breadcrumb(current: FileNode, root: FileNode) -> some View {
        let chain = ([current] + current.ancestors).reversed().filter { $0 === root || $0.isDescendant(of: root) }
        return HStack(spacing: FN.s2) {
            Button { app.treemapRoot = current.parent } label: { Image(systemName: "arrow.up") }
                .buttonStyle(.fn(.secondary, compact: true))
                .disabled(current === root)
                .help("Zoom out")
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 6) {
                    ForEach(Array(chain.enumerated()), id: \.offset) { i, n in
                        if i > 0 { Text("/").font(FN.mono(11)).foregroundStyle(FN.hair) }
                        Button(n.displayName) { app.treemapRoot = n }
                            .buttonStyle(.plain)
                            .font(FN.mono(11, n === current ? .semibold : .regular))
                            .foregroundStyle(n === current ? FN.fg : FN.muted)
                    }
                }
            }
            Spacer()
            Text(Fmt.bytes(current.size(app.sizeMode))).fnData()
            Text("Double-click to zoom").fnLabel()
        }
        .padding(.horizontal, FN.s5).padding(.vertical, FN.s1)
    }

    private var legend: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: FN.s3) {
                ForEach(FileCategory.allCases) { c in
                    HStack(spacing: 6) {
                        Rectangle().fill(c.color).frame(width: 9, height: 9)
                        Text(c.title).fnLabel()
                    }
                }
                HStack(spacing: 6) {
                    Rectangle().fill(FN.track).frame(width: 9, height: 9).fnBorder(FN.hair)
                    Text("Folder (too small to split)").fnLabel()
                }
            }
            .padding(.horizontal, FN.s5).padding(.vertical, 10)
        }
    }
}

import AppKit
import SwiftUI

final class ClosureMenuItem: NSMenuItem {
    private let handler: () -> Void

    init(_ title: String, symbol: String? = nil, enabled: Bool = true, handler: @escaping () -> Void) {
        self.handler = handler
        super.init(title: title, action: #selector(fire), keyEquivalent: "")
        target = self
        isEnabled = enabled
        if let symbol { image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil) }
    }

    required init(coder: NSCoder) { fatalError("not used") }

    @objc private func fire() { handler() }
}

/// Builds the shared right-click menu used by the tree, treemap and file lists.
@MainActor
enum ItemMenu {
    static func populate(_ menu: NSMenu, nodes: [FileNode], app: AppState) {
        menu.removeAllItems()
        guard let first = nodes.first else { return }
        let single = nodes.count == 1
        let paths = nodes.map(\.path)
        menu.addItem(ClosureMenuItem("Reveal in Finder", symbol: "folder") { RealUser.reveal(paths) })
        if single {
            menu.addItem(ClosureMenuItem("Open", symbol: "arrow.up.forward.app") { RealUser.open(first.path) })
            menu.addItem(ClosureMenuItem("Quick Look", symbol: "eye") { RealUser.quickLook(first.path) })
        }
        menu.addItem(ClosureMenuItem(single ? "Copy Path" : "Copy Paths", symbol: "doc.on.clipboard") {
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(paths.joined(separator: "\n"), forType: .string)
        })
        menu.addItem(.separator())
        if single && first.isDirectory {
            menu.addItem(ClosureMenuItem("Scan This Folder", symbol: "scope") { app.scan(first.path) })
            menu.addItem(ClosureMenuItem("Rescan This Folder", symbol: "arrow.clockwise") { app.rescan(first) })
        }
        if single {
            menu.addItem(ClosureMenuItem("Show in Tree", symbol: "list.bullet.indent") { app.showInTree(first) })
            menu.addItem(ClosureMenuItem("Show in Treemap", symbol: "square.grid.3x3.square") { app.showInTreemap(first) })
        }
        if single && first.isDirectory {
            menu.addItem(ClosureMenuItem("Exclude from Future Scans", symbol: "eye.slash") { app.excludeFromScans(first.path) })
        }
        menu.addItem(.separator())
        let verdicts = nodes.map { SafetyClassifier.classify(node: $0) }
        let allBlocked = verdicts.allSatisfy { $0.level == .protected }
        let worst = verdicts.map(\.level).max() ?? .safe
        let header = NSMenuItem(title: "Safety: \(worst.title)", action: nil, keyEquivalent: "")
        header.image = NSImage(systemSymbolName: worst.symbol, accessibilityDescription: nil)?
            .withSymbolConfiguration(.init(paletteColors: [worst.nsColor]))
        header.isEnabled = false
        menu.addItem(header)
        menu.addItem(ClosureMenuItem("Move to Trash…", symbol: "trash", enabled: !allBlocked) {
            app.requestDelete(nodes, permanent: false)
        })
        menu.addItem(ClosureMenuItem("Delete Permanently…", symbol: "xmark.bin", enabled: !allBlocked) {
            app.requestDelete(nodes, permanent: true)
        })
    }
}

struct TreeOutlineView: NSViewRepresentable {
    let app: AppState
    let root: FileNode?
    let treeVersion: Int
    let sortStamp: Int
    let sizeMode: SizeMode
    let selection: [FileNode]

    func makeCoordinator() -> Coordinator { Coordinator(app: app) }

    func makeNSView(context: Context) -> NSScrollView {
        let c = context.coordinator
        let outline = NSOutlineView()
        outline.usesAlternatingRowBackgroundColors = false
        outline.backgroundColor = FN.NS.bg
        outline.allowsMultipleSelection = true
        outline.rowHeight = 26
        outline.style = .fullWidth
        outline.gridStyleMask = []
        outline.intercellSpacing = NSSize(width: 10, height: 0)
        outline.columnAutoresizingStyle = .firstColumnOnlyAutoresizingStyle
        outline.indentationPerLevel = 16
        outline.headerView = FNHeaderView(frame: NSRect(x: 0, y: 0, width: 100, height: 30))
        outline.cornerView = nil
        outline.selectionHighlightStyle = .regular

        func col(_ id: String, _ title: String, width: CGFloat, min: CGFloat, sortKey: String?, ascending: Bool = false, align: NSTextAlignment = .left) {
            let col = NSTableColumn(identifier: .init(id))
            let header = FNHeaderCell(textCell: title)
            header.alignment = align
            header.sortKey = sortKey
            col.headerCell = header
            col.width = width
            col.minWidth = min
            if let sortKey { col.sortDescriptorPrototype = NSSortDescriptor(key: sortKey, ascending: ascending) }
            outline.addTableColumn(col)
        }
        col("name", "Name", width: 340, min: 180, sortKey: "name", ascending: true)
        col("size", sizeMode.title, width: 96, min: 80, sortKey: "size", align: .right)
        col("percent", "Share of Parent", width: 150, min: 100, sortKey: "share")
        col("other", sizeMode.other.title, width: 96, min: 70, sortKey: "other", align: .right)
        col("files", "Files", width: 76, min: 50, sortKey: "files", align: .right)
        col("folders", "Folders", width: 70, min: 50, sortKey: "folders", align: .right)
        col("modified", "Modified", width: 150, min: 90, sortKey: "modified")
        col("owner", "Owner", width: 90, min: 50, sortKey: "owner")
        col("safety", "Safety", width: 104, min: 80, sortKey: "safety")
        outline.outlineTableColumn = outline.tableColumns[0]
        outline.sortDescriptors = [NSSortDescriptor(key: "size", ascending: false)]
        outline.autosaveName = "DiskWatchTreeColumnsFN"
        outline.autosaveTableColumns = true

        outline.dataSource = c
        outline.delegate = c
        outline.target = c
        outline.doubleAction = #selector(Coordinator.doubleClick(_:))
        let menu = NSMenu()
        menu.delegate = c
        outline.menu = menu
        c.outline = outline
        app.revealHandler = { [weak c] node in c?.reveal(node) }

        let scroll = NSScrollView()
        scroll.documentView = outline
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = true
        scroll.autohidesScrollers = true
        scroll.drawsBackground = true
        scroll.backgroundColor = FN.NS.bg
        scroll.scrollerKnobStyle = .light
        c.sync(root: root, version: treeVersion, sortStamp: sortStamp, mode: sizeMode, selection: selection)
        return scroll
    }

    func updateNSView(_ nsView: NSScrollView, context: Context) {
        let c = context.coordinator
        c.app = app
        app.revealHandler = { [weak c] node in c?.reveal(node) }
        c.sync(root: root, version: treeVersion, sortStamp: sortStamp, mode: sizeMode, selection: selection)
    }

    @MainActor
    final class Coordinator: NSObject, NSOutlineViewDataSource, NSOutlineViewDelegate, NSMenuDelegate {
        var app: AppState
        weak var outline: NSOutlineView?
        private weak var lastRoot: FileNode?
        private var lastVersion = -1
        private var lastSort = -1
        private var lastMode: SizeMode?
        private var syncingSelection = false
        private var sortKey = "size"
        private var ascending = false

        init(app: AppState) { self.app = app }

        func sync(root: FileNode?, version: Int, sortStamp: Int, mode: SizeMode, selection: [FileNode]) {
            guard let outline else { return }
            if lastMode != mode {
                lastMode = mode
                outline.tableColumn(withIdentifier: .init("size"))?.headerCell.stringValue = mode.title
                outline.tableColumn(withIdentifier: .init("other"))?.headerCell.stringValue = mode.other.title
                outline.headerView?.needsDisplay = true
            }
            if root !== lastRoot {
                lastRoot = root
                lastVersion = version
                lastSort = sortStamp
                outline.reloadData()
                if let root { outline.expandItem(root) }
            } else if version != lastVersion || sortStamp != lastSort {
                lastVersion = version
                lastSort = sortStamp
                outline.reloadData()
            }
            // Mirror selection coming from other views.
            let current = Set(outline.selectedRowIndexes.compactMap { outline.item(atRow: $0) as? FileNode })
            if current != Set(selection) {
                let rows = IndexSet(selection.map { outline.row(forItem: $0) }.filter { $0 >= 0 })
                syncingSelection = true
                outline.selectRowIndexes(rows, byExtendingSelection: false)
                syncingSelection = false
            }
        }

        func reveal(_ node: FileNode) {
            guard let outline else { return }
            for a in node.ancestors.reversed() { outline.expandItem(a) }
            let row = outline.row(forItem: node)
            guard row >= 0 else { return }
            syncingSelection = true
            outline.selectRowIndexes([row], byExtendingSelection: false)
            syncingSelection = false
            outline.scrollRowToVisible(row)
        }

        private func sorted(_ n: FileNode) -> [FileNode] {
            if n.sortStamp != app.sortStamp {
                let mode = app.sizeMode
                let asc = ascending
                func cmp<T: Comparable>(_ a: T, _ b: T) -> Bool { asc ? a < b : a > b }
                switch sortKey {
                case "name": n.children.sort { a, b in
                    let r = a.name.localizedStandardCompare(b.name)
                    return asc ? r == .orderedAscending : r == .orderedDescending
                }
                case "other": n.children.sort { cmp($0.size(mode.other), $1.size(mode.other)) }
                case "files": n.children.sort { cmp($0.isDirectory ? $0.fileCount : 1, $1.isDirectory ? $1.fileCount : 1) }
                case "folders": n.children.sort { cmp($0.dirCount, $1.dirCount) }
                case "modified": n.children.sort { cmp($0.modified, $1.modified) }
                case "owner": n.children.sort { cmp(Fmt.user($0.uid), Fmt.user($1.uid)) }
                case "safety": n.children.sort { cmp(SafetyClassifier.classify(node: $0).level, SafetyClassifier.classify(node: $1).level) }
                default: n.children.sort { cmp($0.size(mode), $1.size(mode)) }
                }
                n.sortStamp = app.sortStamp
            }
            return n.children
        }

        // MARK: Data source

        func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
            guard let root = lastRoot else { return 0 }
            guard let n = item as? FileNode else { return 1 }
            _ = root
            return sorted(n).count
        }

        func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
            guard let n = item as? FileNode else { return lastRoot! }
            return sorted(n)[index]
        }

        func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
            guard let n = item as? FileNode else { return false }
            return n.isDirectory && !n.children.isEmpty
        }

        func outlineView(_ outlineView: NSOutlineView, sortDescriptorsDidChange oldDescriptors: [NSSortDescriptor]) {
            guard let d = outlineView.sortDescriptors.first, let key = d.key else { return }
            sortKey = key
            ascending = d.ascending
            app.sortStamp += 1
        }

        // MARK: Delegate

        func outlineViewSelectionDidChange(_ notification: Notification) {
            guard !syncingSelection, let outline else { return }
            app.selection = outline.selectedRowIndexes.compactMap { outline.item(atRow: $0) as? FileNode }
        }

        func outlineView(_ outlineView: NSOutlineView, rowViewForItem item: Any) -> NSTableRowView? {
            outlineView.makeView(withIdentifier: .init("row"), owner: nil) as? FNRowView ?? {
                let r = FNRowView()
                r.identifier = .init("row")
                return r
            }()
        }

        func outlineView(_ outlineView: NSOutlineView, viewFor tableColumn: NSTableColumn?, item: Any) -> NSView? {
            guard let n = item as? FileNode, let id = tableColumn?.identifier.rawValue else { return nil }
            let mode = app.sizeMode
            switch id {
            case "name":
                let cell = reuse(outlineView, "name") { NameCell() }
                cell.configure(n)
                return cell
            case "percent":
                let cell = reuse(outlineView, "share") { ShareCell() }
                cell.fraction = n.parent == nil ? 1 : n.fraction(ofParent: mode)
                cell.text = n.parent == nil ? "" : Fmt.percent(n.fraction(ofParent: mode))
                cell.dim = n.hardlinkDuplicate || n.excluded
                cell.needsDisplay = true
                return cell
            case "safety":
                let cell = reuse(outlineView, "safety") { SafetyCell() }
                cell.configure(SafetyClassifier.classify(node: n))
                return cell
            default:
                let cell = reuse(outlineView, "text") { TextCell() }
                var align = NSTextAlignment.right
                var primary = false
                let s: String
                switch id {
                case "size": s = Fmt.bytes(n.size(mode)); primary = true
                case "other": s = Fmt.bytes(n.size(mode.other))
                case "files": s = n.isDirectory ? Fmt.count(n.fileCount) : ""
                case "folders": s = n.isDirectory ? Fmt.count(n.dirCount) : ""
                case "modified": s = Fmt.date(n.modified); align = .left
                case "owner": s = Fmt.user(n.uid); align = .left
                default: s = ""
                }
                cell.configure(s, align: align, primary: primary)
                return cell
            }
        }

        private func reuse<T: NSView>(_ ov: NSOutlineView, _ id: String, make: () -> T) -> T {
            if let v = ov.makeView(withIdentifier: .init(id), owner: nil) as? T { return v }
            let v = make()
            v.identifier = .init(id)
            return v
        }

        @objc func doubleClick(_ sender: NSOutlineView) {
            let row = sender.clickedRow
            guard row >= 0, let n = sender.item(atRow: row) as? FileNode else { return }
            if n.isDirectory && !n.children.isEmpty {
                if sender.isItemExpanded(n) { sender.collapseItem(n) } else { sender.expandItem(n) }
            } else {
                RealUser.quickLook(n.path)
            }
        }

        // MARK: Menu

        func menuNeedsUpdate(_ menu: NSMenu) {
            guard let outline else { return }
            let clicked = outline.clickedRow
            var nodes: [FileNode] = []
            if clicked >= 0 {
                if outline.selectedRowIndexes.contains(clicked) {
                    nodes = outline.selectedRowIndexes.compactMap { outline.item(atRow: $0) as? FileNode }
                } else if let n = outline.item(atRow: clicked) as? FileNode {
                    nodes = [n]
                }
            }
            ItemMenu.populate(menu, nodes: nodes, app: app)
        }
    }
}

// MARK: - Chrome & cells (FloydNet Terminal)

/// Header strip: black, uppercase micro-labels, hairline rule beneath.
final class FNHeaderView: NSTableHeaderView {
    override func draw(_ dirtyRect: NSRect) {
        FN.NS.bg.setFill()
        bounds.fill()
        guard let tv = tableView else { return }
        for (i, col) in tv.tableColumns.enumerated() {
            let r = headerRect(ofColumn: i)
            if r.intersects(dirtyRect) { col.headerCell.draw(withFrame: r, in: self) }
        }
        FN.NS.border.setFill()
        NSRect(x: 0, y: bounds.maxY - 1, width: bounds.width, height: 1).fill()
    }
}

final class FNHeaderCell: NSTableHeaderCell {
    var sortKey: String?

    override func draw(withFrame cellFrame: NSRect, in controlView: NSView) {
        FN.NS.bg.setFill()
        cellFrame.fill()
        let sorted = (controlView as? NSTableHeaderView)?.tableView?.sortDescriptors.first
        let active = sortKey != nil && sorted?.key == sortKey
        let attrs: [NSAttributedString.Key: Any] = [
            .font: FN.nsMono(10, .semibold),
            .kern: 1.0,
            .foregroundColor: active ? FN.NS.fg : FN.NS.muted,
        ]
        let str = NSAttributedString(string: stringValue.uppercased(), attributes: attrs)
        let size = str.size()
        let inset = cellFrame.insetBy(dx: 6, dy: 0)
        let arrowW: CGFloat = active ? 10 : 0
        var x = inset.minX
        if alignment == .right { x = inset.maxX - size.width - arrowW }
        let y = cellFrame.midY - size.height / 2 - 1
        str.draw(at: NSPoint(x: x, y: y))
        if active, let sorted {
            let ax = x + size.width + 4
            let ay = cellFrame.midY - 1
            let p = NSBezierPath()
            if sorted.ascending {
                p.move(to: NSPoint(x: ax, y: ay + 2)); p.line(to: NSPoint(x: ax + 6, y: ay + 2)); p.line(to: NSPoint(x: ax + 3, y: ay - 2))
            } else {
                p.move(to: NSPoint(x: ax, y: ay - 2)); p.line(to: NSPoint(x: ax + 6, y: ay - 2)); p.line(to: NSPoint(x: ax + 3, y: ay + 2))
            }
            p.close()
            FN.NS.accent.setFill()
            p.fill()
        }
        FN.NS.border.setFill()
        NSRect(x: cellFrame.maxX - 1, y: cellFrame.minY + 8, width: 1, height: cellFrame.height - 16).fill()
    }

    override func drawSortIndicator(withFrame cellFrame: NSRect, in controlView: NSView, ascending: Bool, priority: Int) {}
}

/// Row: black with a hairline rule; selection inverts the whole row to accent.
final class FNRowView: NSTableRowView {
    override func drawBackground(in dirtyRect: NSRect) {
        FN.NS.bg.setFill()
        bounds.fill()
        FN.NS.border.setFill()
        NSRect(x: 0, y: bounds.height - 1, width: bounds.width, height: 1).fill()
    }

    override func drawSelection(in dirtyRect: NSRect) {
        FN.NS.accent.setFill()
        bounds.fill()
    }

    override var isEmphasized: Bool {
        get { true }
        set {}
    }

    override var interiorBackgroundStyle: NSView.BackgroundStyle { isSelected ? .emphasized : .normal }
}

/// Mixin: cells repaint when their row is selected (text goes `bg` on the accent fill).
class FNCell: NSTableCellView {
    var selected: Bool { backgroundStyle == .emphasized }
    override var backgroundStyle: NSView.BackgroundStyle {
        didSet { applyStyle(); needsDisplay = true }
    }
    func applyStyle() {}
}

final class NameCell: FNCell {
    private let icon = NSImageView()
    private let label = NSTextField(labelWithString: "")
    private let badge = NSImageView()
    private var dim = false
    private var isRoot = false

    override init(frame: NSRect) {
        super.init(frame: frame)
        for v in [icon, label, badge] as [NSView] {
            v.translatesAutoresizingMaskIntoConstraints = false
            addSubview(v)
        }
        icon.imageScaling = .scaleProportionallyDown
        label.lineBreakMode = .byTruncatingMiddle
        imageView = icon
        textField = label
        NSLayoutConstraint.activate([
            icon.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 2),
            icon.centerYAnchor.constraint(equalTo: centerYAnchor),
            icon.widthAnchor.constraint(equalToConstant: 14),
            icon.heightAnchor.constraint(equalToConstant: 14),
            label.leadingAnchor.constraint(equalTo: icon.trailingAnchor, constant: 8),
            label.centerYAnchor.constraint(equalTo: centerYAnchor),
            badge.leadingAnchor.constraint(equalTo: label.trailingAnchor, constant: 6),
            badge.centerYAnchor.constraint(equalTo: centerYAnchor),
            badge.widthAnchor.constraint(equalToConstant: 11),
            badge.heightAnchor.constraint(equalToConstant: 11),
            badge.trailingAnchor.constraint(lessThanOrEqualTo: trailingAnchor, constant: -2),
        ])
        label.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    func configure(_ n: FileNode) {
        let cfg = NSImage.SymbolConfiguration(pointSize: 11, weight: .regular)
        icon.image = NSImage(systemSymbolName: FNIcon.symbol(for: n), accessibilityDescription: nil)?.withSymbolConfiguration(cfg)
        var name = n.displayName
        if n.excluded { name += "  [skipped]" }
        if n.hardlinkDuplicate { name += "  [hard link]" }
        label.stringValue = name
        dim = n.excluded || n.hardlinkDuplicate || n.unreadable
        isRoot = n.parent == nil
        label.font = FN.nsMono(12, isRoot ? .semibold : .regular)
        if n.unreadable {
            badge.image = NSImage(systemSymbolName: "lock.fill", accessibilityDescription: "Unreadable")
            badge.toolTip = "Could not be read — grant Full Disk Access to include it."
            badge.isHidden = false
        } else if n.isSymlink {
            badge.image = NSImage(systemSymbolName: "arrow.turn.up.right", accessibilityDescription: "Symlink")
            badge.toolTip = "Symbolic link (target not counted)"
            badge.isHidden = false
        } else {
            badge.isHidden = true
        }
        applyStyle()
    }

    override func applyStyle() {
        if selected {
            label.textColor = FN.NS.bg
            icon.contentTintColor = FN.NS.bg
            badge.contentTintColor = FN.NS.bg
        } else {
            label.textColor = dim ? FN.NS.muted : FN.NS.fg
            icon.contentTintColor = isRoot ? FN.NS.accent : FN.NS.muted
            badge.contentTintColor = FN.NS.warn
        }
    }
}

final class TextCell: FNCell {
    private var primary = false

    override init(frame: NSRect) {
        super.init(frame: frame)
        let tf = NSTextField(labelWithString: "")
        tf.translatesAutoresizingMaskIntoConstraints = false
        tf.lineBreakMode = .byTruncatingTail
        addSubview(tf)
        textField = tf
        NSLayoutConstraint.activate([
            tf.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 2),
            tf.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -2),
            tf.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    func configure(_ s: String, align: NSTextAlignment, primary: Bool) {
        self.primary = primary
        textField?.stringValue = s
        textField?.alignment = align
        textField?.font = FN.nsMono(12, primary ? .semibold : .regular)
        applyStyle()
    }

    override func applyStyle() {
        textField?.textColor = selected ? FN.NS.bg : (primary ? FN.NS.fg : FN.NS.muted)
    }
}

/// Flat share-of-parent bar plus percentage. No rounding, no gradient.
final class ShareCell: FNCell {
    var fraction: Double = 0
    var text = ""
    var dim = false

    override func draw(_ dirtyRect: NSRect) {
        let textW: CGFloat = 48
        let bar = NSRect(x: 2, y: bounds.midY - 3, width: max(0, bounds.width - textW - 8), height: 6)
        (selected ? FN.NS.accentDim : FN.NS.track).setFill()
        bar.fill()
        let w = bar.width * CGFloat(min(1, max(0, fraction)))
        if w > 0 {
            (selected ? FN.NS.bg : (dim ? FN.NS.hair : FN.NS.accent)).setFill()
            NSRect(x: bar.minX, y: bar.minY, width: max(1, w), height: bar.height).fill()
        }
        let attrs: [NSAttributedString.Key: Any] = [
            .font: FN.nsMono(11),
            .foregroundColor: selected ? FN.NS.bg : FN.NS.muted,
        ]
        let s = NSAttributedString(string: text, attributes: attrs)
        let size = s.size()
        s.draw(at: NSPoint(x: bounds.maxX - size.width - 2, y: bounds.midY - size.height / 2))
    }
}

/// Outlined status pill (protected is the one filled state).
final class SafetyCell: FNCell {
    private var level: SafetyLevel = .safe

    func configure(_ v: SafetyVerdict) {
        level = v.level
        toolTip = v.level.title + "\n" + v.reasons.joined(separator: "\n")
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        let filled = level == .protected
        let color: NSColor = selected ? FN.NS.bg : level.nsColor
        let attrs: [NSAttributedString.Key: Any] = [
            .font: FN.nsMono(9, .semibold),
            .kern: 0.6,
            .foregroundColor: filled ? (selected ? FN.NS.accent : FN.NS.bg) : color,
        ]
        let s = NSAttributedString(string: level.shortTitle.uppercased(), attributes: attrs)
        let size = s.size()
        let box = NSRect(x: 2.5, y: (bounds.midY - size.height / 2 - 2.5).rounded() + 0.5, width: (size.width + 12).rounded(), height: (size.height + 5).rounded())
        if filled {
            color.setFill()
            box.fill()
        } else {
            color.setStroke()
            let p = NSBezierPath(rect: box)
            p.lineWidth = 1
            p.stroke()
        }
        s.draw(at: NSPoint(x: box.minX + 6, y: box.minY + 2.5))
    }
}

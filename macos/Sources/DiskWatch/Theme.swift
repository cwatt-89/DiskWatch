import AppKit
import SwiftUI

// FloydNet Terminal design system — tokens and components.
// Black ground, hairline compartments, zero radius, no shadows/gradients, one green accent,
// stretched Times New Roman for identity moments over a monospace body.

enum FN {
    // MARK: Color tokens
    static let bg = Color(hex: 0x000000)
    static let surface = Color(hex: 0x060606)
    static let fg = Color(hex: 0xF2F2F2)
    static let muted = Color(hex: 0x8A8A8A)
    static let border = Color(hex: 0x2A2A2A)
    static let hair = Color(hex: 0x444444)
    static let accent = Color(hex: 0x17D97A)
    static let accentDim = Color(hex: 0x0E8A4F)
    static let accentBright = Color(hex: 0x3DFFA0)
    static let warn = Color(hex: 0xD1901F)
    static let danger = Color(hex: 0xFF3B30)
    /// Gauge track fill (bundle.css `.fnt-gauge-bar`).
    static let track = Color(hex: 0x111111)

    enum NS {
        static let bg = NSColor(hex: 0x000000)
        static let surface = NSColor(hex: 0x060606)
        static let fg = NSColor(hex: 0xF2F2F2)
        static let muted = NSColor(hex: 0x8A8A8A)
        static let border = NSColor(hex: 0x2A2A2A)
        static let hair = NSColor(hex: 0x444444)
        static let accent = NSColor(hex: 0x17D97A)
        static let accentDim = NSColor(hex: 0x0E8A4F)
        static let accentBright = NSColor(hex: 0x3DFFA0)
        static let warn = NSColor(hex: 0xD1901F)
        static let danger = NSColor(hex: 0xFF3B30)
        static let track = NSColor(hex: 0x111111)
    }

    // MARK: Spacing
    static let s1: CGFloat = 8
    static let s2: CGFloat = 12
    static let s3: CGFloat = 16
    static let s4: CGFloat = 20
    static let s5: CGFloat = 24
    static let s6: CGFloat = 28
    static let s7: CGFloat = 40
    static let s8: CGFloat = 56

    // MARK: Type
    static func mono(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight, design: .monospaced)
    }
    static func nsMono(_ size: CGFloat, _ weight: NSFont.Weight = .regular) -> NSFont {
        .monospacedSystemFont(ofSize: size, weight: weight)
    }
    static let displayFamily = "Times New Roman"
}

extension Color {
    init(hex: UInt32) {
        self.init(.sRGB, red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255,
                  blue: Double(hex & 0xFF) / 255, opacity: 1)
    }
}

extension NSColor {
    convenience init(hex: UInt32) {
        self.init(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
                  blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
    }
}

// MARK: - Text styles

extension View {
    /// body — 14px mono.
    func fnBody(_ color: Color = FN.fg) -> some View { font(FN.mono(14)).foregroundStyle(color) }
    /// Dense data rows — 12px mono (tables).
    func fnData(_ color: Color = FN.fg) -> some View { font(FN.mono(12)).foregroundStyle(color) }
    /// value — 24px mono.
    func fnValue(_ color: Color = FN.fg) -> some View { font(FN.mono(24)).foregroundStyle(color) }
    /// caption — 12px, .03em.
    func fnCaption(_ color: Color = FN.muted) -> some View { font(FN.mono(12)).tracking(0.36).foregroundStyle(color) }
    /// small — 11px semibold uppercase, .06em.
    func fnSmall(_ color: Color = FN.fg) -> some View {
        font(FN.mono(11, .semibold)).tracking(0.66).textCase(.uppercase).foregroundStyle(color)
    }
    /// label — 10px semibold uppercase, .1em.
    func fnLabel(_ color: Color = FN.muted) -> some View {
        font(FN.mono(10, .semibold)).tracking(1.0).textCase(.uppercase).foregroundStyle(color)
    }

    /// Hairline border on every edge (sharp corners).
    func fnBorder(_ color: Color = FN.border, _ width: CGFloat = 1) -> some View {
        overlay(Rectangle().strokeBorder(color, lineWidth: width))
    }
}

/// Times New Roman, vertically stretched — the system's signature move. `scaleEffect` doesn't
/// change layout, so the extra height is added back as padding.
struct Stretched: View {
    let text: String
    var size: CGFloat
    var scale: CGFloat
    var tracking: CGFloat = 0
    var uppercase = false
    var color: Color = FN.fg
    /// Optional trailing mark in its own color (e.g. the wordmark's accent period).
    var suffix: String?
    var suffixColor: Color = FN.accent

    var body: some View {
        (Text(uppercase ? text.uppercased() : text).foregroundColor(color)
            + Text(suffix ?? "").foregroundColor(suffixColor))
            .font(.custom(FN.displayFamily, size: size))
            .tracking(tracking * size)
            .lineLimit(1)
            .scaleEffect(x: 1, y: scale, anchor: .leading)
            .padding(.vertical, size * (scale - 1) / 2)
    }
}

struct FNWordmark: View {
    var text = "DiskWatch"
    var size: CGFloat = 42
    var body: some View { Stretched(text: text, size: size, scale: 1.55, tracking: -0.01, suffix: ".") }
}

struct FNHeading: View {
    let text: String
    var body: some View { Stretched(text: text, size: 24, scale: 1.4, tracking: 0.01, uppercase: true) }
}

struct FNHeroValue: View {
    let text: String
    var color: Color = FN.fg
    var body: some View { Stretched(text: text, size: 34, scale: 1.55, color: color) }
}

/// Uppercase stretched-serif heading with the hairline rule beneath it.
struct FNSectionHeading<Trailing: View>: View {
    let title: String
    var caption: String?
    @ViewBuilder var trailing: Trailing

    var body: some View {
        HStack(alignment: .bottom, spacing: FN.s3) {
            VStack(alignment: .leading, spacing: 6) {
                FNHeading(text: title)
                    .padding(.bottom, 12)
                    .overlay(alignment: .bottom) { Rectangle().fill(FN.border).frame(height: 1) }
                    .fixedSize()
                if let caption { Text(caption).fnCaption() }
            }
            Spacer(minLength: 0)
            trailing
        }
    }
}

extension FNSectionHeading where Trailing == EmptyView {
    init(title: String, caption: String? = nil) {
        self.init(title: title, caption: caption) { EmptyView() }
    }
}

/// Section — the primary layout compartment.
struct FNSection<Trailing: View, Content: View>: View {
    let title: String?
    var caption: String?
    var padding: CGFloat = FN.s6
    @ViewBuilder var trailing: Trailing
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: FN.s5) {
            if let title { FNSectionHeading(title: title, caption: caption) { trailing } }
            content
        }
        .padding(padding)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .background(FN.surface)
        .fnBorder()
    }
}

extension FNSection where Trailing == EmptyView {
    init(title: String?, caption: String? = nil, padding: CGFloat = FN.s6, @ViewBuilder content: () -> Content) {
        self.init(title: title, caption: caption, padding: padding, trailing: { EmptyView() }, content: content)
    }
}

// MARK: - Buttons

enum FNButtonKind { case primary, secondary, danger, ghost }

struct FNButtonStyle: ButtonStyle {
    var kind: FNButtonKind = .secondary
    var compact = false

    func makeBody(configuration: Configuration) -> some View {
        StyledBody(configuration: configuration, kind: kind, compact: compact)
    }

    private struct StyledBody: View {
        let configuration: Configuration
        let kind: FNButtonKind
        let compact: Bool
        @Environment(\.isEnabled) private var enabled
        @State private var hover = false

        var body: some View {
            let active = enabled && (hover || configuration.isPressed)
            let (fg, bg, stroke): (Color, Color, Color) = {
                switch kind {
                case .primary: return active ? (FN.bg, FN.accent, FN.accent) : (FN.bg, FN.fg, FN.fg)
                case .secondary: return active ? (FN.bg, FN.accent, FN.accent) : (FN.fg, FN.bg, FN.hair)
                case .danger: return active ? (FN.bg, FN.danger, FN.danger) : (FN.danger, FN.bg, FN.danger)
                case .ghost: return (active ? FN.accent : FN.muted, .clear, .clear)
                }
            }()
            configuration.label
                .font(FN.mono(11, .semibold))
                .tracking(0.88)
                .textCase(.uppercase)
                .labelStyle(FNLabelStyle())
                .lineLimit(1)
                .foregroundStyle(fg)
                .padding(.horizontal, kind == .ghost ? 4 : (compact ? 10 : 14))
                .padding(.vertical, kind == .ghost ? 2 : (compact ? 6 : 9))
                .background(bg)
                .overlay(Rectangle().strokeBorder(stroke, lineWidth: 1))
                .contentShape(Rectangle())
                .opacity(enabled ? 1 : 0.35)
                .onHover { hover = $0 }
        }
    }
}

struct FNLabelStyle: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: FN.s1) {
            configuration.icon.font(.system(size: 11, weight: .semibold))
            configuration.title
        }
    }
}

extension ButtonStyle where Self == FNButtonStyle {
    static var fnPrimary: FNButtonStyle { FNButtonStyle(kind: .primary) }
    static var fnSecondary: FNButtonStyle { FNButtonStyle(kind: .secondary) }
    static var fnDanger: FNButtonStyle { FNButtonStyle(kind: .danger) }
    static var fnGhost: FNButtonStyle { FNButtonStyle(kind: .ghost) }
    static func fn(_ kind: FNButtonKind, compact: Bool = false) -> FNButtonStyle { FNButtonStyle(kind: kind, compact: compact) }
}

/// Inline text link (accent on hover), for "Open in Tree"-style jumps.
struct FNLinkStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View { StyledBody(configuration: configuration) }
    private struct StyledBody: View {
        let configuration: Configuration
        @State private var hover = false
        var body: some View {
            configuration.label
                .font(FN.mono(11, .semibold)).tracking(0.66).textCase(.uppercase)
                .foregroundStyle(hover ? FN.accent : FN.fg)
                .overlay(alignment: .bottom) { Rectangle().fill(hover ? FN.accent : FN.hair).frame(height: 1).offset(y: 3) }
                .onHover { hover = $0 }
        }
    }
}

// MARK: - Pill

/// `inverse` is for pills drawn inside an accent-filled (selected) row.
enum FNPillState { case neutral, normal, ok, warn, danger, inverse }

struct FNPill: View {
    let text: String
    var state: FNPillState = .normal
    var symbol: String?

    var body: some View {
        let (fg, bg, stroke): (Color, Color, Color) = {
            switch state {
            case .neutral: return (FN.muted, .clear, FN.hair)
            case .normal: return (FN.fg, .clear, FN.fg)
            case .ok: return (FN.accent, .clear, FN.accent)
            case .warn: return (FN.warn, .clear, FN.warn)
            case .danger: return (FN.bg, FN.danger, FN.danger)
            case .inverse: return (FN.bg, .clear, FN.bg)
            }
        }()
        HStack(spacing: 5) {
            if let symbol { Image(systemName: symbol).font(.system(size: 8, weight: .bold)) }
            Text(text)
        }
        .font(FN.mono(10, .semibold)).tracking(0.6).textCase(.uppercase)
        .lineLimit(1)
        .foregroundStyle(fg)
        .padding(.horizontal, 8).padding(.vertical, 3)
        .background(bg)
        .overlay(Rectangle().strokeBorder(stroke, lineWidth: 1))
        .fixedSize()
    }
}

// MARK: - Gauge

enum FNGaugeState {
    case ok, warn, danger
    var color: Color { self == .ok ? FN.accent : self == .warn ? FN.warn : FN.danger }
    var pill: FNPillState { self == .ok ? .ok : self == .warn ? .warn : .danger }
    static func of(_ fraction: Double, threshold: Double) -> FNGaugeState {
        fraction > threshold ? .danger : fraction > threshold - 0.1 ? .warn : .ok
    }
}

/// Horizontal capacity bar with a fixed accent-bright threshold marker.
struct FNGauge: View {
    let fraction: Double
    var threshold: Double? = 0.9
    var height: CGFloat = 18
    var color: Color?
    var secondary: Double?

    var body: some View {
        GeometryReader { g in
            let w = g.size.width
            let f = min(1, max(0, fraction))
            let state = FNGaugeState.of(fraction, threshold: threshold ?? 1.01)
            ZStack(alignment: .leading) {
                Rectangle().fill(FN.track)
                if let secondary {
                    Rectangle().fill(FN.hair).frame(width: w * min(1, max(0, secondary)))
                }
                Rectangle().fill(color ?? state.color).frame(width: w * f)
                if let threshold {
                    Rectangle().fill(FN.accentBright).frame(width: 2, height: height + (height >= 12 ? 8 : 4))
                        .offset(x: w * threshold - 1)
                }
            }
            .frame(height: height)
            .overlay(Rectangle().strokeBorder(FN.hair, lineWidth: 1))
        }
        .frame(height: height)
    }
}

/// Thin flat bar for inline size shares (no threshold).
struct FNBar: View {
    let fraction: Double
    var color: Color = FN.accent
    var height: CGFloat = 6

    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Rectangle().fill(FN.track)
                Rectangle().fill(color).frame(width: max(fraction > 0 ? 1 : 0, g.size.width * min(1, max(0, fraction))))
            }
        }
        .frame(height: height)
    }
}

// MARK: - Stat tile

struct FNStatTile: View {
    let label: String
    let value: String
    var detail: String?
    var valueColor: Color = FN.fg

    var body: some View {
        VStack(alignment: .leading, spacing: FN.s1) {
            Text(label).fnLabel()
            Text(value).fnValue(valueColor).lineLimit(1).minimumScaleFactor(0.6)
            if let detail { Text(detail).fnCaption().lineLimit(1) }
        }
        .padding(FN.s4)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(FN.surface)
        .fnBorder()
    }
}

// MARK: - Controls

/// Sharp segmented control; the selected segment is the accent (active state).
struct FNSegmented<T: Hashable>: View {
    @Binding var selection: T
    let items: [(T, String)]
    var disabled = false

    var body: some View {
        HStack(spacing: -1) {
            ForEach(Array(items.enumerated()), id: \.offset) { _, item in
                Segment(title: item.1, selected: selection == item.0) { selection = item.0 }
            }
        }
        .disabled(disabled)
        .opacity(disabled ? 0.35 : 1)
        .fixedSize()
    }

    private struct Segment: View {
        let title: String
        let selected: Bool
        let action: () -> Void
        @State private var hover = false

        var body: some View {
            Button(action: action) {
                Text(title)
                    .font(FN.mono(11, .semibold)).tracking(0.88).textCase(.uppercase)
                    .foregroundStyle(selected ? FN.bg : (hover ? FN.fg : FN.muted))
                    .padding(.horizontal, 12).padding(.vertical, 7)
                    .background(selected ? FN.accent : FN.bg)
                    .overlay(Rectangle().strokeBorder(selected ? FN.accent : FN.hair, lineWidth: 1))
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .onHover { hover = $0 }
            .zIndex(selected ? 1 : 0)
        }
    }
}

/// Square checkbox: hair outline, accent fill with a bg-colored check when on.
struct FNCheckboxStyle: ToggleStyle {
    func makeBody(configuration: Configuration) -> some View {
        StyledBody(configuration: configuration)
    }
    private struct StyledBody: View {
        let configuration: Configuration
        @Environment(\.isEnabled) private var enabled
        @State private var hover = false
        var body: some View {
            Button { configuration.isOn.toggle() } label: {
                HStack(spacing: FN.s1) {
                    ZStack {
                        Rectangle().fill(configuration.isOn ? FN.accent : FN.bg)
                        Rectangle().strokeBorder(configuration.isOn ? FN.accent : (hover ? FN.fg : FN.hair), lineWidth: 1)
                        if configuration.isOn {
                            Image(systemName: "checkmark").font(.system(size: 9, weight: .heavy)).foregroundStyle(FN.bg)
                        }
                    }
                    .frame(width: 14, height: 14)
                    configuration.label.fnData()
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .opacity(enabled ? 1 : 0.35)
            .onHover { hover = $0 }
        }
    }
}

extension ToggleStyle where Self == FNCheckboxStyle {
    static var fnCheckbox: FNCheckboxStyle { FNCheckboxStyle() }
}

/// Sharp text field with a hair outline that turns accent while focused.
struct FNTextField: View {
    let placeholder: String
    @Binding var text: String
    var symbol: String?
    var width: CGFloat?
    @FocusState private var focused: Bool

    var body: some View {
        HStack(spacing: FN.s1) {
            if let symbol { Image(systemName: symbol).font(.system(size: 11)).foregroundStyle(FN.muted) }
            TextField("", text: $text, prompt: Text(placeholder).foregroundStyle(FN.muted))
                .textFieldStyle(.plain)
                .font(FN.mono(12))
                .foregroundStyle(FN.fg)
                .focused($focused)
            if !text.isEmpty {
                Button { text = "" } label: { Image(systemName: "xmark").font(.system(size: 9, weight: .bold)) }
                    .buttonStyle(.fnGhost)
            }
        }
        .padding(.horizontal, 10).padding(.vertical, 7)
        .frame(width: width)
        .background(FN.bg)
        .overlay(Rectangle().strokeBorder(focused ? FN.accent : FN.hair, lineWidth: 1))
    }
}

/// Dropdown with a sharp bordered label; the menu itself is the native (dark) menu.
struct FNMenuPicker<T: Hashable>: View {
    var label: String?
    @Binding var selection: T
    let options: [(T, String)]

    var body: some View {
        HStack(spacing: FN.s1) {
            if let label { Text(label).fnLabel().fixedSize() }
            Menu {
                ForEach(Array(options.enumerated()), id: \.offset) { _, o in
                    Button {
                        selection = o.0
                    } label: {
                        if o.0 == selection { Label(o.1, systemImage: "checkmark") } else { Text(o.1) }
                    }
                }
            } label: {
                HStack(spacing: 6) {
                    Text(options.first { $0.0 == selection }?.1 ?? "—")
                        .font(FN.mono(11, .semibold)).tracking(0.66).textCase(.uppercase)
                        .foregroundStyle(FN.fg)
                    Image(systemName: "chevron.down").font(.system(size: 8, weight: .bold)).foregroundStyle(FN.muted)
                }
                .padding(.horizontal, 10).padding(.vertical, 7)
                .background(FN.bg)
                .overlay(Rectangle().strokeBorder(FN.hair, lineWidth: 1))
                .contentShape(Rectangle())
            }
            .menuStyle(.button)
            .buttonStyle(.plain)
            .menuIndicator(.hidden)
            .fixedSize()
        }
    }
}

/// 1px rule in `border`.
struct FNRule: View {
    var vertical = false
    var color: Color = FN.border
    var body: some View {
        Rectangle().fill(color).frame(width: vertical ? 1 : nil, height: vertical ? nil : 1)
    }
}

/// Monochrome SF Symbol for a node (the system has no colored iconography).
enum FNIcon {
    static func symbol(for n: FileNode) -> String {
        if n.isSymlink { return "arrow.turn.up.right" }
        if n.isDirectory {
            if n.nameExtension == "app" { return "app" }
            if n.isPackage { return "shippingbox" }
            return n.parent == nil ? "internaldrive" : "folder"
        }
        return FileCategory.of(n).symbol
    }

    static func symbol(forPath path: String) -> String {
        var st = stat()
        if lstat(path, &st) == 0 && (st.st_mode & S_IFMT) == S_IFDIR { return "folder" }
        return "doc"
    }
}

/// Selection helper for custom lists: click, ⌘-click toggle, ⇧-click range.
struct FNListSelection {
    static func click<ID: Hashable>(_ id: ID, order: [ID], selection: inout Set<ID>, anchor: inout ID?) {
        let flags = NSEvent.modifierFlags
        if flags.contains(.shift), let a = anchor, let i = order.firstIndex(of: a), let j = order.firstIndex(of: id) {
            selection = Set(order[min(i, j)...max(i, j)])
        } else if flags.contains(.command) {
            if selection.contains(id) { selection.remove(id) } else { selection.insert(id) }
            anchor = id
        } else {
            selection = [id]
            anchor = id
        }
    }
}

/// Row background: accent when selected (inverted), track grey on hover.
struct FNRowBackground: ViewModifier {
    let selected: Bool
    @State private var hover = false

    func body(content: Content) -> some View {
        content
            .background(selected ? FN.accent : (hover ? FN.track : FN.bg))
            .overlay(alignment: .bottom) { Rectangle().fill(FN.border).frame(height: 1) }
            .environment(\.fnSelected, selected)
            .onHover { hover = $0 }
    }
}

private struct FNSelectedKey: EnvironmentKey { static let defaultValue = false }

extension EnvironmentValues {
    /// True inside a selected (accent-filled) row, so content can invert to `bg`.
    var fnSelected: Bool {
        get { self[FNSelectedKey.self] }
        set { self[FNSelectedKey.self] = newValue }
    }
}

/// Sortable column header cell for custom lists.
struct FNColumnHeader: View {
    let title: String
    var active = false
    var ascending = false
    var alignment: Alignment = .leading
    var action: (() -> Void)?

    var body: some View {
        Button { action?() } label: {
            HStack(spacing: 4) {
                if alignment == .trailing { Spacer(minLength: 0) }
                Text(title).fnLabel(active ? FN.fg : FN.muted)
                if active {
                    Image(systemName: ascending ? "arrowtriangle.up.fill" : "arrowtriangle.down.fill")
                        .font(.system(size: 6)).foregroundStyle(FN.accent)
                }
                if alignment == .leading { Spacer(minLength: 0) }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .disabled(action == nil)
    }
}

/// In-window modal: dims the window and shows a sharp bordered panel (replaces rounded sheets).
struct FNModal<Content: View>: View {
    var width: CGFloat = 640
    @ViewBuilder var content: Content

    var body: some View {
        ZStack {
            Rectangle().fill(FN.bg.opacity(0.8)).ignoresSafeArea()
                .contentShape(Rectangle())
                .onTapGesture {}
            content
                .padding(FN.s6)
                .frame(width: width)
                .background(FN.surface)
                .fnBorder(FN.hair)
        }
    }
}

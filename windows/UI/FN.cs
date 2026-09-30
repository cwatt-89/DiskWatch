using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using DiskWatch.Core;
using Path = Avalonia.Controls.Shapes.Path;

namespace DiskWatch.UI;

// FloydNet Terminal design system — tokens and components.
// Black ground, hairline compartments, zero radius, no shadows/gradients, one green accent,
// stretched Times New Roman for identity moments over a monospace body.
public static class FN
{
    // MARK: Color tokens
    public static readonly Color BgC = Color.FromUInt32(0xFF000000);
    public static readonly IBrush Bg = B(0x000000), Surface = B(0x060606), Fg = B(0xF2F2F2), Muted = B(0x8A8A8A),
        Line = B(0x2A2A2A), Hair = B(0x444444), Accent = B(0x17D97A), AccentDim = B(0x0E8A4F), AccentBright = B(0x3DFFA0),
        Warn = B(0xD1901F), Danger = B(0xFF3B30), Track = B(0x111111), Scrim = new ImmutableSolidColorBrush(Color.FromArgb(204, 0, 0, 0));

    public static IBrush B(uint rgb) => new ImmutableSolidColorBrush(Color.FromUInt32(0xFF000000 | rgb));

    // MARK: Spacing
    public const double S1 = 8, S2 = 12, S3 = 16, S4 = 20, S5 = 24, S6 = 28, S7 = 40, S8 = 56;

    // MARK: Type
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, SF Mono, Menlo, monospace");
    public static readonly FontFamily Display = new("Times New Roman, Times, serif");

    public static TextBlock T(string text, double size = 12, IBrush? color = null, FontWeight weight = FontWeight.Normal,
                              double tracking = 0, bool upper = false, string? cls = null, bool wrap = false)
    {
        var tb = new TextBlock
        {
            Text = upper ? text.ToUpperInvariant() : text,
            FontFamily = Mono,
            FontSize = size,
            FontWeight = weight,
            LetterSpacing = tracking,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (cls != null) tb.Classes.Add(cls); // class-driven color (inverts inside selected rows)
        else tb.Foreground = color ?? Fg;
        return tb;
    }

    /// <summary>body — 14px.</summary>
    public static TextBlock Body(string t, IBrush? c = null) => T(t, 14, c ?? Fg);
    /// <summary>Dense data — 12px.</summary>
    public static TextBlock Data(string t, IBrush? c = null) => T(t, 12, c ?? Fg);
    /// <summary>value — 24px.</summary>
    public static TextBlock Value(string t, IBrush? c = null) => T(t, 24, c ?? Fg);
    /// <summary>caption — 12px, .03em.</summary>
    public static TextBlock Caption(string t, IBrush? c = null, bool wrap = true) => T(t, 12, c ?? Muted, tracking: 0.36, wrap: wrap);
    /// <summary>small — 11px semibold uppercase, .06em.</summary>
    public static TextBlock Small(string t, IBrush? c = null) => T(t, 11, c ?? Fg, FontWeight.SemiBold, 0.66, true);
    /// <summary>label — 10px semibold uppercase, .1em.</summary>
    public static TextBlock Label(string t, IBrush? c = null) => T(t, 10, c ?? Muted, FontWeight.SemiBold, 1.0, true);

    /// <summary>Times New Roman, vertically stretched — the signature move. Extra height is added back as margin.</summary>
    public static Control Stretched(string text, double size, double scale, double tracking = 0, bool upper = false,
                                    IBrush? color = null, string? suffix = null, IBrush? suffixColor = null)
    {
        var tb = new TextBlock
        {
            FontFamily = Display,
            FontSize = size,
            LetterSpacing = tracking * size,
            Foreground = color ?? Fg,
            TextWrapping = TextWrapping.NoWrap,
            RenderTransform = new ScaleTransform(1, scale),
            RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        tb.Inlines = new InlineCollection { new Run(upper ? text.ToUpperInvariant() : text) };
        if (suffix != null) tb.Inlines.Add(new Run(suffix) { Foreground = suffixColor ?? Accent });
        double lineH = size * 1.15;
        return new Decorator { Child = tb, Margin = new Thickness(0, lineH * (scale - 1) / 2) };
    }

    public static Control Wordmark(double size = 42) => Stretched("DiskWatch", size, 1.55, -0.01, suffix: ".");
    public static Control Heading(string text) => Stretched(text, 24, 1.4, 0.01, upper: true);
    public static Control Hero(string text, IBrush? color = null) => Stretched(text, 34, 1.55, color: color);

    /// <summary>Uppercase stretched-serif heading with the hairline rule beneath it.</summary>
    public static Control SectionHeading(string title, string? caption = null, Control? trailing = null)
    {
        var left = new StackPanel { Spacing = 6 };
        left.Children.Add(new Border
        {
            Child = Heading(title),
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        if (caption != null) left.Children.Add(Caption(caption));
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(left);
        if (trailing != null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Bottom;
            trailing.Margin = new Thickness(S3, 0, 0, 0);
            Grid.SetColumn(trailing, 1);
            grid.Children.Add(trailing);
        }
        return grid;
    }

    /// <summary>Section — the primary layout compartment.</summary>
    public static Border Section(string? title, Control content, string? caption = null, Control? trailing = null, double padding = S6)
    {
        var stack = new StackPanel { Spacing = S5 };
        if (title != null) stack.Children.Add(SectionHeading(title, caption, trailing));
        stack.Children.Add(content);
        return new Border { Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(padding), Child = stack };
    }

    public static Border Rule(bool vertical = false, IBrush? color = null) =>
        new() { Background = color ?? Line, Width = vertical ? 1 : double.NaN, Height = vertical ? double.NaN : 1 };

    public static StackPanel H(double spacing, params Control[] kids)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
        foreach (var k in kids) s.Children.Add(k);
        return s;
    }

    public static StackPanel V(double spacing, params Control[] kids)
    {
        var s = new StackPanel { Spacing = spacing };
        foreach (var k in kids) s.Children.Add(k);
        return s;
    }

    /// <summary>Leading fixed parts, then text that gets the remaining width (so it trims/wraps instead of overflowing).</summary>
    public static Grid IconRow(double spacing, params Control[] parts)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", parts.Select((_, i) => i == parts.Length - 1 ? "*" : "Auto"))), VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) parts[i].Margin = new Thickness(spacing, 0, 0, 0);
            if (i < parts.Length - 1) parts[i].VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(parts[i], i);
            g.Children.Add(parts[i]);
        }
        return g;
    }

    /// <summary>Row with a stretching first column and auto-sized columns after it.</summary>
    public static Grid Row(Control stretch, params Control[] rest)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*" + string.Concat(Enumerable.Repeat(",Auto", rest.Length))) };
        g.Children.Add(stretch);
        for (int i = 0; i < rest.Length; i++)
        {
            Grid.SetColumn(rest[i], i + 1);
            rest[i].Margin = new Thickness(S1, 0, 0, 0);
            rest[i].VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(rest[i]);
        }
        return g;
    }

    public static Control Swatch(IBrush fill, double size = 9, IBrush? stroke = null) =>
        new Border { Width = size, Height = size, Background = fill, BorderBrush = stroke, BorderThickness = new Thickness(stroke == null ? 0 : 1), VerticalAlignment = VerticalAlignment.Center };

    // MARK: Pill

    public enum PillState { Neutral, Normal, Ok, Warn, Danger }

    public static PillState Pill(this SafetyLevel l) => l switch
    {
        SafetyLevel.Safe => PillState.Ok,
        SafetyLevel.Caution => PillState.Normal,
        SafetyLevel.Danger => PillState.Warn,
        _ => PillState.Danger,
    };

    public static IBrush Tint(this SafetyLevel l) => l switch
    {
        SafetyLevel.Safe => Accent,
        SafetyLevel.Caution => Fg,
        SafetyLevel.Danger => Warn,
        _ => Danger,
    };

    /// <summary>Outlined status pill; styled by classes so it inverts inside selected rows.</summary>
    public static Border Pill(string text, PillState state)
    {
        var b = new Border { Child = T(text, 10, weight: FontWeight.SemiBold, tracking: 0.6, upper: true, cls: "pilltext"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add("pill");
        b.Classes.Add(state.ToString().ToLowerInvariant());
        return b;
    }

    // MARK: Stat tile

    public static Border StatTile(string label, string value, string? detail = null, IBrush? valueColor = null)
    {
        var s = V(S1, Label(label), T(value, 24, valueColor ?? Fg));
        if (detail != null) s.Children.Add(Caption(detail, wrap: false));
        return new Border { Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(S4), Child = s };
    }

    /// <summary>Evenly filled row of tiles (never leaves a half-empty row).</summary>
    public static Grid TileRow(params Control[] tiles)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", tiles.Select(_ => "*"))) };
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].Margin = new Thickness(i == 0 ? 0 : S3 / 2, 0, i == tiles.Length - 1 ? 0 : S3 / 2, 0);
            Grid.SetColumn(tiles[i], i);
            g.Children.Add(tiles[i]);
        }
        return g;
    }

    // MARK: Icons (stroked line glyphs on a 24-unit grid; no colored iconography)

    public static readonly Dictionary<string, string> Glyphs = new()
    {
        ["folder"] = "M2,6 L9,6 L11,8 L22,8 L22,19 L2,19 Z",
        ["doc"] = "M5,2 L14,2 L19,7 L19,22 L5,22 Z M14,2 L14,7 L19,7",
        ["drive"] = "M2,8 L22,8 L22,16 L2,16 Z M16,12 L18,12",
        ["box"] = "M3,7 L12,3 L21,7 L21,17 L12,21 L3,17 Z M3,7 L12,11 L21,7 M12,11 L12,21",
        ["lock"] = "M6,11 L18,11 L18,21 L6,21 Z M8,11 L8,7 A4,4 0 0 1 16,7 L16,11",
        ["link"] = "M5,19 L5,11 L17,11 M13,7 L17,11 L13,15",
        ["search"] = "M10,3 A7,7 0 1 1 9.99,3 Z M15,15 L21,21",
        ["refresh"] = "M20,12 A8,8 0 1 1 17.6,6.3 M20,3 L20,8 L15,8",
        ["export"] = "M12,3 L12,15 M7,8 L12,3 L17,8 M4,14 L4,21 L20,21 L20,14",
        ["panel"] = "M3,4 L21,4 L21,20 L3,20 Z M15,4 L15,20",
        ["up"] = "M12,20 L12,4 M6,10 L12,4 L18,10",
        ["x"] = "M6,6 L18,18 M18,6 L6,18",
        ["check"] = "M4,12 L10,18 L20,6",
        ["right"] = "M9,5 L16,12 L9,19",
        ["down"] = "M5,9 L12,16 L19,9",
        ["home"] = "M3,11 L12,3 L21,11 M5,9 L5,21 L19,21 L19,9",
        ["trash"] = "M4,6 L20,6 M9,6 L9,3 L15,3 L15,6 M6,6 L7,21 L17,21 L18,6",
        ["clock"] = "M12,3 A9,9 0 1 1 11.99,3 Z M12,7 L12,12 L15,14",
        ["eye"] = "M2,12 Q12,3 22,12 Q12,21 2,12 Z M12,9 A3,3 0 1 1 11.99,9 Z",
        ["stop"] = "M6,6 L18,6 L18,18 L6,18 Z",
        ["grid"] = "M4,4 L10,4 L10,10 L4,10 Z M14,4 L20,4 L20,10 L14,10 Z M4,14 L10,14 L10,20 L4,20 Z M14,14 L20,14 L20,20 L14,20 Z",
        ["download"] = "M12,3 L12,15 M7,10 L12,15 L17,10 M4,20 L20,20",
        ["books"] = "M4,4 L8,4 L8,20 L4,20 Z M10,4 L14,4 L14,20 L10,20 Z M16,5 L20,4 L22,19 L18,20 Z",
        ["users"] = "M9,5 A3,3 0 1 1 8.99,5 Z M3,20 Q9,11 15,20 M16,5 A3,3 0 0 1 16,11 M17,14 Q21,15 21,20",
        ["hammer"] = "M14,4 L20,10 L17,13 L11,7 Z M12,9 L4,17 L7,20 L15,12",
        ["folderplus"] = "M2,6 L9,6 L11,8 L22,8 L22,19 L2,19 Z M12,11 L12,17 M9,14 L15,14",
        ["warn"] = "M12,3 L22,20 L2,20 Z M12,9 L12,14 M12,16.5 L12,17.5",
        ["monitor"] = "M3,4 L21,4 L21,16 L3,16 Z M8,20 L16,20 M12,16 L12,20",
        ["spark"] = "M12,3 L14,10 L21,12 L14,14 L12,21 L10,14 L3,12 L10,10 Z",
        ["app"] = "M4,4 L20,4 L20,20 L4,20 Z M4,9 L20,9",
        ["open"] = "M14,4 L20,4 L20,10 M20,4 L11,13 M18,14 L18,20 L4,20 L4,6 L10,6",
        ["film"] = "M3,5 L21,5 L21,19 L3,19 Z M7,5 L7,19 M17,5 L17,19",
        ["music"] = "M9,18 A3,3 0 1 1 8.99,18 M12,18 L12,4 L20,6",
        ["image"] = "M3,5 L21,5 L21,19 L3,19 Z M3,16 L9,10 L14,15 L17,12 L21,16",
        ["archive"] = "M3,4 L21,4 L21,8 L3,8 Z M5,8 L5,20 L19,20 L19,8 M10,12 L14,12",
        ["disc"] = "M12,3 A9,9 0 1 1 11.99,3 Z M12,10 A2,2 0 1 1 11.99,10 Z",
        ["code"] = "M8,7 L3,12 L8,17 M16,7 L21,12 L16,17",
        ["gear"] = "M12,8 A4,4 0 1 1 11.99,8 Z M12,2 L12,5 M12,19 L12,22 M2,12 L5,12 M19,12 L22,12",
        ["question"] = "M4,4 L20,4 L20,20 L4,20 Z M9,9 Q9,6 12,6 Q15,6 15,9 Q15,11 12,12 L12,14 M12,16.5 L12,17.5",
    };

    public static Path Icon(string name, double size = 12, IBrush? stroke = null, string? cls = null)
    {
        var p = new Path
        {
            Data = Geometry.Parse(Glyphs.GetValueOrDefault(name, Glyphs["doc"])),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Square,
            StrokeJoin = PenLineJoin.Miter,
            VerticalAlignment = VerticalAlignment.Center,
        };
        p.Classes.Add("icon");
        if (cls != null) p.Classes.Add(cls);
        else p.Stroke = stroke ?? Muted;
        return p;
    }

    public static string IconFor(FileNode n)
    {
        if (n.IsLink) return "link";
        if (n.IsDirectory) return n.Parent == null ? "drive" : "folder";
        return IconFor(FileCategoryExt.Of(n));
    }

    public static string IconFor(FileCategory c) => c switch
    {
        FileCategory.Video => "film",
        FileCategory.Audio => "music",
        FileCategory.Images => "image",
        FileCategory.Documents => "doc",
        FileCategory.Archives => "archive",
        FileCategory.Installers => "disc",
        FileCategory.Applications => "app",
        FileCategory.Code => "code",
        FileCategory.VirtualMachines => "monitor",
        FileCategory.System => "gear",
        _ => "question",
    };

    public static IBrush Brush(this FileCategory c) => B(c.Hex());
    public static IBrush Ink(this FileCategory c) => c.DarkInk() ? Bg : Fg;

    // MARK: Controls

    public static FNButton Button(string text, FNButton.Kind kind, Action onClick, string? icon = null, bool compact = false, string? tip = null)
    {
        var b = new FNButton(text, kind, icon, compact);
        b.Click += onClick;
        if (tip != null) ToolTip.SetTip(b, tip);
        return b;
    }

    public static FNButton IconButton(string icon, FNButton.Kind kind, Action onClick, string? tip = null)
    {
        var b = new FNButton(null, kind, icon, false);
        b.Click += onClick;
        if (tip != null) ToolTip.SetTip(b, tip);
        return b;
    }

    /// <summary>Inline text link (accent on hover), for "Open in Tree"-style jumps.</summary>
    public static Control Link(string text, Action onClick)
    {
        var tb = Small(text);
        var under = new Border { Child = tb, BorderBrush = Hair, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 2), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
        under.PointerEntered += (_, _) => { tb.Foreground = Accent; under.BorderBrush = Accent; };
        under.PointerExited += (_, _) => { tb.Foreground = Fg; under.BorderBrush = Hair; };
        under.Tapped += (_, _) => onClick();
        return under;
    }

    /// <summary>Sharp segmented control; the selected segment is the accent (active state).</summary>
    public static Control Segmented<TV>(IEnumerable<(TV Value, string Title)> items, TV selected, Action<TV> onSelect, bool disabled = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = -1, Opacity = disabled ? 0.35 : 1, IsHitTestVisible = !disabled };
        foreach (var (value, title) in items)
        {
            bool sel = EqualityComparer<TV>.Default.Equals(value, selected);
            var tb = T(title, 11, sel ? Bg : Muted, FontWeight.SemiBold, 0.88, true);
            var cell = new Border
            {
                Child = tb,
                Padding = new Thickness(12, 7),
                Background = sel ? Accent : Bg,
                BorderBrush = sel ? Accent : Hair,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                ZIndex = sel ? 1 : 0,
            };
            if (!sel)
            {
                cell.PointerEntered += (_, _) => tb.Foreground = Fg;
                cell.PointerExited += (_, _) => tb.Foreground = Muted;
            }
            cell.Tapped += (_, _) => onSelect(value);
            panel.Children.Add(cell);
        }
        return panel;
    }

    /// <summary>Square checkbox: hair outline, accent fill with a bg-colored check when on.</summary>
    public static Control Checkbox(bool on, Action<bool> onChange, string? label = null, bool enabled = true)
    {
        var box = new Border
        {
            Width = 14, Height = 14,
            Background = on ? Accent : Bg,
            BorderBrush = on ? Accent : Hair,
            BorderThickness = new Thickness(1),
            Child = on ? Icon("check", 9, Bg) : null,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = H(S1, box);
        if (label != null) row.Children.Add(Data(label));
        var host = new Border { Child = row, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Opacity = enabled ? 1 : 0.35, IsHitTestVisible = enabled };
        host.PointerEntered += (_, _) => { if (!on) box.BorderBrush = Fg; };
        host.PointerExited += (_, _) => { if (!on) box.BorderBrush = Hair; };
        host.Tapped += (_, e) => { e.Handled = true; onChange(!on); };
        return host;
    }

    /// <summary>Sharp text field with a hair outline that turns accent while focused (see App.axaml).</summary>
    public static TextBox TextField(string placeholder, string text, Action<string> onChange, double width = double.NaN)
    {
        var tb = new TextBox { Watermark = placeholder, Text = text, Width = width };
        tb.TextChanged += (_, _) => onChange(tb.Text ?? "");
        return tb;
    }

    /// <summary>Dropdown with a sharp bordered label; the menu is styled to match (App.axaml).</summary>
    public static Control MenuPicker<TV>(string? label, IEnumerable<(TV Value, string Title)> options, TV selected, Action<TV> onSelect)
    {
        var opts = options.ToList();
        var current = opts.FirstOrDefault(o => EqualityComparer<TV>.Default.Equals(o.Value, selected)).Title ?? "—";
        var chip = new Border
        {
            Child = H(6, Small(current), Icon("down", 8, Muted)),
            Padding = new Thickness(10, 7),
            Background = Bg,
            BorderBrush = Hair,
            BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        chip.PointerEntered += (_, _) => chip.BorderBrush = Fg;
        chip.PointerExited += (_, _) => chip.BorderBrush = Hair;
        chip.Tapped += (_, _) =>
        {
            var flyout = new MenuFlyout();
            foreach (var (v, t) in opts)
            {
                var mi = new MenuItem { Header = t, Icon = EqualityComparer<TV>.Default.Equals(v, selected) ? Icon("check", 10, Accent) : null };
                mi.Click += (_, _) => onSelect(v);
                flyout.Items.Add(mi);
            }
            flyout.ShowAt(chip);
        };
        return label == null ? chip : H(S1, Label(label), chip);
    }

    /// <summary>Sortable column header cell for lists.</summary>
    public static Control ColumnHeader(string title, bool active = false, bool ascending = false, bool right = false, Action? onClick = null)
    {
        var tb = Label(title, active ? Fg : Muted);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        row.Children.Add(tb);
        if (active)
        {
            row.Children.Add(new Path
            {
                Data = Geometry.Parse(ascending ? "M0,5 L6,5 L3,0 Z" : "M0,0 L6,0 L3,5 Z"),
                Fill = Accent, Width = 6, Height = 5, VerticalAlignment = VerticalAlignment.Center,
            });
        }
        var host = new Border { Child = row, Background = Brushes.Transparent };
        if (onClick != null)
        {
            host.Cursor = new Cursor(StandardCursorType.Hand);
            host.Tapped += (_, _) => onClick();
        }
        return host;
    }

    /// <summary>Scroll container with the page ground.</summary>
    public static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
}

/// <summary>Button: primary (light→accent), secondary (hair outline→accent), danger (red outline→red fill), ghost (icon only).</summary>
public sealed class FNButton : Border
{
    public enum Kind { Primary, Secondary, Danger, Ghost }

    public event Action? Click;
    private readonly Kind _kind;
    private readonly TextBlock? _text;
    private readonly Path? _icon;
    private bool _hover, _pressed;

    public FNButton(string? text, Kind kind, string? icon, bool compact)
    {
        _kind = kind;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = FN.S1, HorizontalAlignment = HorizontalAlignment.Center };
        if (icon != null) { _icon = FN.Icon(icon, 11); row.Children.Add(_icon); }
        if (text != null) { _text = FN.T(text, 11, weight: FontWeight.SemiBold, tracking: 0.88, upper: true); row.Children.Add(_text); }
        Child = row;
        Padding = kind == Kind.Ghost ? new Thickness(4, 2) : compact ? new Thickness(10, 6) : new Thickness(14, 9);
        BorderThickness = new Thickness(1);
        Cursor = new Cursor(StandardCursorType.Hand);
        VerticalAlignment = VerticalAlignment.Center;
        PointerEntered += (_, _) => { _hover = true; Apply(); };
        PointerExited += (_, _) => { _hover = false; _pressed = false; Apply(); };
        PointerPressed += (_, e) => { _pressed = true; Apply(); e.Handled = true; };
        PointerReleased += (_, e) =>
        {
            bool fire = _pressed && IsEffectivelyEnabled;
            _pressed = false;
            Apply();
            if (fire) { e.Handled = true; Click?.Invoke(); }
        };
        PropertyChanged += (_, e) => { if (e.Property == IsEnabledProperty) Apply(); };
        Apply();
    }

    public void Invoke() { if (IsEffectivelyEnabled) Click?.Invoke(); }

    private void Apply()
    {
        bool active = IsEnabled && (_hover || _pressed);
        (IBrush fg, IBrush bg, IBrush stroke) = _kind switch
        {
            Kind.Primary => active ? (FN.Bg, FN.Accent, FN.Accent) : (FN.Bg, FN.Fg, FN.Fg),
            Kind.Secondary => active ? (FN.Bg, FN.Accent, FN.Accent) : (FN.Fg, FN.Bg, FN.Hair),
            Kind.Danger => active ? (FN.Bg, FN.Danger, FN.Danger) : (FN.Danger, FN.Bg, FN.Danger),
            _ => (active ? FN.Accent : FN.Muted, Brushes.Transparent, Brushes.Transparent),
        };
        if (_text != null) _text.Foreground = fg;
        if (_icon != null) _icon.Stroke = fg;
        Background = bg;
        BorderBrush = stroke;
        Opacity = IsEnabled ? 1 : 0.35;
    }
}

/// <summary>Horizontal capacity bar with a fixed accent-bright threshold marker.</summary>
public sealed class Gauge : Control
{
    public double Fraction { get; set; }
    public double? Threshold { get; set; } = 0.9;
    public IBrush? FillOverride { get; set; }
    public double? Secondary { get; set; }
    public double BarHeight { get; set; } = 18;

    public Gauge() { ClipToBounds = false; }

    public static IBrush StateBrush(double f, double threshold) => f > threshold ? FN.Danger : f > threshold - 0.1 ? FN.Warn : FN.Accent;

    protected override Size MeasureOverride(Size available) => new(0, BarHeight);

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var r = new Rect(0, 0, w, BarHeight);
        ctx.FillRectangle(FN.Track, r);
        if (Secondary is double s) ctx.FillRectangle(FN.Hair, new Rect(0, 0, w * Math.Clamp(s, 0, 1), BarHeight));
        ctx.FillRectangle(FillOverride ?? StateBrush(Fraction, Threshold ?? 1.01), new Rect(0, 0, w * Math.Clamp(Fraction, 0, 1), BarHeight));
        ctx.DrawRectangle(new Pen(FN.Hair, 1), r.Deflate(0.5));
        if (Threshold is double t)
        {
            double ext = BarHeight >= 12 ? 4 : 2;
            ctx.FillRectangle(FN.AccentBright, new Rect(w * t - 1, -ext, 2, BarHeight + ext * 2));
        }
    }
}

/// <summary>Thin flat bar for inline size shares. Inverts inside selected rows.</summary>
public sealed class Bar : Control
{
    public static readonly StyledProperty<bool> SelectedProperty = AvaloniaProperty.Register<Bar, bool>(nameof(Selected));
    public bool Selected { get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    public double Fraction { get; set; }
    public IBrush Fill { get; set; } = FN.Accent;
    public double BarHeight { get; set; } = 6;

    static Bar() { AffectsRender<Bar>(SelectedProperty); }

    protected override Size MeasureOverride(Size available) => new(0, BarHeight);

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        double y = (Bounds.Height - BarHeight) / 2;
        ctx.FillRectangle(Selected ? FN.AccentDim : FN.Track, new Rect(0, y, w, BarHeight));
        double fw = w * Math.Clamp(Fraction, 0, 1);
        if (Fraction > 0) ctx.FillRectangle(Selected ? FN.Bg : Fill, new Rect(0, y, Math.Max(1, fw), BarHeight));
    }
}

/// <summary>Stacked, flat composition bar — segments separated by 1px of the black ground.</summary>
public sealed class CompositionBar : Control
{
    public List<(IBrush Fill, double Fraction)> Segments { get; set; } = new();
    public double BarHeight { get; set; } = 18;

    protected override Size MeasureOverride(Size available) => new(0, BarHeight);

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        ctx.FillRectangle(FN.Track, new Rect(0, 0, w, BarHeight));
        double x = 0;
        foreach (var (fill, f) in Segments)
        {
            double sw = f * w;
            if (sw >= 1) ctx.FillRectangle(fill, new Rect(x, 0, Math.Max(1, sw - 1), BarHeight));
            x += sw;
        }
        ctx.DrawRectangle(new Pen(FN.Hair, 1), new Rect(0, 0, w, BarHeight).Deflate(0.5));
    }
}

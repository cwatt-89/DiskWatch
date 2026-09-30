using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DiskWatch.Core;

namespace DiskWatch.UI;

public readonly record struct Tile(FileNode Node, Rect Rect, int Depth, bool IsLeaf);

public static class TreemapLayout
{
    public const int MaxTiles = 25_000;

    public static List<Tile> Build(FileNode root, Size size, SizeMode mode)
    {
        var outp = new List<Tile>(4096);
        Layout(root, new Rect(0, 0, size.Width, size.Height).Deflate(1), 0, mode, outp);
        return outp;
    }

    private static void Layout(FileNode node, Rect rect, int depth, SizeMode mode, List<Tile> outp)
    {
        if (rect.Width < 2 || rect.Height < 2) return;
        var all = node.Children.Where(c => c.Size(mode) > 0).OrderByDescending(c => c.Size(mode)).ToList();
        double total = all.Sum(c => (double)c.Size(mode));
        if (total <= 0) return;
        double scale = rect.Width * rect.Height / total;
        var items = new List<(FileNode, double)>();
        foreach (var c in all)
        {
            double a = c.Size(mode) * scale;
            if (a < 3) break;
            items.Add((c, a));
        }
        foreach (var (child, r) in Squarify(items, rect))
        {
            if (outp.Count >= MaxTiles) return;
            bool canNest = child.IsDirectory && child.Children.Count > 0 && r.Width > 14 && r.Height > 14;
            if (canNest)
            {
                outp.Add(new Tile(child, r, depth, false));
                double header = r.Width > 70 && r.Height > 40 ? 15 : 0;
                Layout(child, new Rect(r.X + 2, r.Y + header + 2, r.Width - 4, r.Height - header - 4), depth + 1, mode, outp);
            }
            else outp.Add(new Tile(child, r, depth, true));
        }
    }

    /// <summary>Squarified treemap (Bruls, Huizing, van Wijk). Items must be sorted largest first.</summary>
    public static List<(FileNode, Rect)> Squarify(List<(FileNode Node, double Area)> items, Rect start)
    {
        var outp = new List<(FileNode, Rect)>();
        var rect = start;
        int i = 0;
        static double Worst(double sum, double big, double small, double w)
        {
            double s2 = sum * sum, w2 = w * w;
            return Math.Max(w2 * big / s2, s2 / (w2 * small));
        }
        while (i < items.Count)
        {
            double w = Math.Min(rect.Width, rect.Height);
            if (w <= 0.5) break;
            double sum = items[i].Area, big = items[i].Area;
            double best = Worst(sum, big, items[i].Area, w);
            int end = i + 1;
            while (end < items.Count)
            {
                double ns = sum + items[end].Area;
                double nw = Worst(ns, big, items[end].Area, w);
                if (nw > best) break;
                sum = ns; best = nw; end++;
            }
            if (rect.Width >= rect.Height)
            {
                double colW = sum / rect.Height, y = rect.Y;
                for (int k = i; k < end; k++)
                {
                    double h = items[k].Area / colW;
                    outp.Add((items[k].Node, new Rect(rect.X, y, colW, h)));
                    y += h;
                }
                rect = new Rect(rect.X + colW, rect.Y, Math.Max(0, rect.Width - colW), rect.Height);
            }
            else
            {
                double rowH = sum / rect.Width, x = rect.X;
                for (int k = i; k < end; k++)
                {
                    double wd = items[k].Area / rowH;
                    outp.Add((items[k].Node, new Rect(x, rect.Y, wd, rowH)));
                    x += wd;
                }
                rect = new Rect(rect.X, rect.Y + rowH, rect.Width, Math.Max(0, rect.Height - rowH));
            }
            i = end;
        }
        return outp;
    }
}

public sealed class TreemapView : DockPanel, IView
{
    private readonly AppState S;
    private readonly ContentControl _crumbs = new();
    private readonly TreemapCanvas _canvas;
    private readonly Border _tip = new() { Background = FN.Bg, BorderBrush = FN.Hair, BorderThickness = new Thickness(1), Padding = new Thickness(FN.S2), Width = 300, IsVisible = false };
    private FileNode? _tipNode;

    private Control TipContent(FileNode n)
    {
        var mode = S.SizeMode;
        return FN.V(6,
            FN.Label(n.IsDirectory ? "Folder" : FileCategoryExt.Of(n).Title()),
            FN.T(n.Name, 12, FN.Fg, FontWeight.SemiBold),
            FN.H(FN.S1, FN.Data(Fmt.Bytes(n.Size(mode)), FN.Accent), FN.T(n.Parent != null ? Fmt.Percent(n.FractionOfParent(mode)) + " of parent" : "", 11, FN.Muted)),
            FN.T(n.IsDirectory ? $"{Fmt.Count(n.FileCount)} files · {Fmt.Count(n.DirCount)} folders" : "Modified " + Fmt.ShortDate(n.ModifiedTicks), 11, FN.Muted),
            FN.T(Fmt.Abbreviate(n.FullPath), 10, FN.Muted));
    }

    public TreemapView(AppState s)
    {
        S = s;
        _canvas = new TreemapCanvas(s);
        var legend = Legend();
        var r1 = FN.Rule();
        var r2 = FN.Rule();
        SetDock(_crumbs, Dock.Top);
        SetDock(r1, Dock.Top);
        SetDock(legend, Dock.Bottom);
        SetDock(r2, Dock.Bottom);
        Children.Add(_crumbs);
        Children.Add(r1);
        Children.Add(legend);
        Children.Add(r2);
        var layer = new Canvas { IsHitTestVisible = false };
        layer.Children.Add(_tip);
        _canvas.Hover += (n, pt) =>
        {
            _tip.IsVisible = n != null;
            if (n == null) return;
            if (!ReferenceEquals(n, _tipNode)) { _tipNode = n; _tip.Child = TipContent(n); }
            double x = Math.Min(Math.Max(8, pt.X + 16), layer.Bounds.Width - 308);
            double y = Math.Min(pt.Y + 16, layer.Bounds.Height - 120);
            Canvas.SetLeft(_tip, x);
            Canvas.SetTop(_tip, Math.Max(8, y));
        };
        Children.Add(new Border { Child = new Grid { Children = { _canvas, layer } }, Padding = new Thickness(FN.S2), Background = FN.Bg });
        Refresh(Topic.Tree);
    }

    private FileNode? Current => S.Root == null ? null
        : S.TreemapRoot is { } t && (ReferenceEquals(t, S.Root) || t.IsDescendantOf(S.Root)) ? t : S.Root;

    public void Refresh(Topic t)
    {
        _crumbs.Content = Breadcrumb();
        _canvas.Current = Current;
        _canvas.InvalidateLayoutCache();
    }

    private Control Breadcrumb()
    {
        var cur = Current;
        if (cur == null || S.Root == null) return new Border();
        var chain = new[] { cur }.Concat(cur.Ancestors).Reverse().Where(n => ReferenceEquals(n, S.Root) || n.IsDescendantOf(S.Root)).ToList();
        var up = FN.IconButton("up", FNButton.Kind.Secondary, () => { S.TreemapRoot = cur.Parent; S.Notify(Topic.Tree); }, "Zoom out");
        up.IsEnabled = !ReferenceEquals(cur, S.Root);
        up.Padding = new Thickness(10, 6);
        var path = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < chain.Count; i++)
        {
            var n = chain[i];
            if (i > 0) path.Children.Add(FN.T(Path.DirectorySeparatorChar.ToString(), 11, FN.Hair));
            var tb = FN.T(n.DisplayName, 11, ReferenceEquals(n, cur) ? FN.Fg : FN.Muted, ReferenceEquals(n, cur) ? FontWeight.SemiBold : FontWeight.Normal);
            var host = new Border { Child = tb, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            host.Tapped += (_, _) => { S.TreemapRoot = n; S.Notify(Topic.Tree); };
            path.Children.Add(host);
        }
        var row = FN.Row(FN.H(FN.S2, up, path), FN.Data(Fmt.Bytes(cur.Size(S.SizeMode))), FN.Label("Double-click to zoom"));
        return new Border { Child = row, Padding = new Thickness(FN.S5, FN.S1) };
    }

    private static Control Legend()
    {
        var p = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in Enum.GetValues<FileCategory>())
            p.Children.Add(new Border { Child = FN.H(6, FN.Swatch(c.Brush()), FN.Label(c.Title())), Margin = new Thickness(0, 0, FN.S3, 0) });
        p.Children.Add(FN.H(6, FN.Swatch(FN.Track, stroke: FN.Hair), FN.Label("Folder (too small to split)")));
        return new Border { Child = p, Padding = new Thickness(FN.S5, 10) };
    }
}

/// <summary>Flat fills only: kinds on the lightness ramp, folders as bordered surface compartments.</summary>
public sealed class TreemapCanvas : Control
{
    private readonly AppState S;
    public FileNode? Current;
    private List<Tile> _tiles = new();
    private string _key = "";
    private FileNode? _hover;
    private Point _hoverPt;
    private static readonly Typeface Semi = new(FN.Mono, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Reg = new(FN.Mono);

    public TreemapCanvas(AppState s)
    {
        S = s;
        ClipToBounds = true;
        Focusable = true;
        PointerMoved += (_, e) =>
        {
            _hoverPt = e.GetPosition(this);
            var hit = Hit(_hoverPt)?.Node;
            if (!ReferenceEquals(hit, _hover)) { _hover = hit; UpdateTip(); }
            PositionTip();
            InvalidateVisual();
        };
        PointerExited += (_, _) => { _hover = null; UpdateTip(); InvalidateVisual(); };
        PointerPressed += (_, e) =>
        {
            var pt = e.GetCurrentPoint(this);
            var t = Hit(pt.Position);
            if (t == null) return;
            if (pt.Properties.IsRightButtonPressed)
            {
                ItemMenu.Build(S, new() { t.Value.Node }).Open(this);
                return;
            }
            if (e.ClickCount >= 2)
            {
                var target = t.Value.Node.IsDirectory ? t.Value.Node : t.Value.Node.Parent;
                if (target != null && target.Children.Count > 0) { S.TreemapRoot = target; S.Notify(Topic.Tree); }
            }
            else S.Select(t.Value.Node);
        };
    }

    public void InvalidateLayoutCache() { _key = ""; InvalidateVisual(); }

    private Tile? Hit(Point p)
    {
        for (int i = _tiles.Count - 1; i >= 0; i--) if (_tiles[i].Rect.Contains(p)) return _tiles[i];
        return null;
    }

    private void EnsureLayout()
    {
        if (Current == null) { _tiles = new(); return; }
        var key = $"{Current.GetHashCode()}|{(int)Bounds.Width}x{(int)Bounds.Height}|{S.SizeMode}|{S.TreeVersion}";
        if (key == _key) return;
        _key = key;
        _tiles = TreemapLayout.Build(Current, Bounds.Size, S.SizeMode);
    }

    private static FormattedText Text(string s, double size, IBrush brush, Typeface face, double maxW)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush)
        {
            MaxTextWidth = Math.Max(1, maxW),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        return ft;
    }

    public override void Render(DrawingContext ctx)
    {
        EnsureLayout();
        ctx.FillRectangle(FN.Bg, new Rect(Bounds.Size));
        var mode = S.SizeMode;
        var hairPen = new Pen(FN.Hair, 1);
        var linePen = new Pen(FN.Line, 1);
        foreach (var t in _tiles)
        {
            var r = t.Rect;
            if (t.IsLeaf)
            {
                var inner = r.Deflate(0.5);
                FileCategory cat = FileCategoryExt.Of(t.Node);
                if (t.Node.IsDirectory)
                {
                    ctx.FillRectangle(FN.Track, inner);
                    ctx.DrawRectangle(hairPen, inner.Deflate(0.5));
                }
                else ctx.FillRectangle(cat.Brush(), inner);
                if (r.Width > 54 && r.Height > 18)
                {
                    var ink = t.Node.IsDirectory ? FN.Fg : cat.Ink();
                    using (ctx.PushClip(r))
                    {
                        ctx.DrawText(Text(t.Node.Name, 10, ink, Semi, r.Width - 8), new Point(r.X + 4, r.Y + 3));
                        if (r.Height > 34) ctx.DrawText(Text(Fmt.Bytes(t.Node.Size(mode)), 9, ink, Reg, r.Width - 8), new Point(r.X + 4, r.Y + 17));
                    }
                }
            }
            else
            {
                ctx.FillRectangle(FN.Surface, r);
                ctx.DrawRectangle(linePen, r.Deflate(0.5));
                if (r.Width > 70 && r.Height > 40)
                {
                    using (ctx.PushClip(r))
                        ctx.DrawText(Text($"{t.Node.Name.ToUpperInvariant()}  {Fmt.Bytes(t.Node.Size(mode))}", 10, FN.Muted, Semi, r.Width - 10), new Point(r.X + 5, r.Y + 2));
                }
            }
        }
        if (_hover != null)
            foreach (var t in _tiles) if (ReferenceEquals(t.Node, _hover)) ctx.DrawRectangle(new Pen(FN.Fg, 1), t.Rect.Deflate(0.5));
        var sel = S.Selection.ToHashSet();
        var selPen = new Pen(FN.AccentBright, 2);
        foreach (var t in _tiles) if (sel.Contains(t.Node)) ctx.DrawRectangle(selPen, t.Rect.Deflate(1));
        if (_tiles.Count == 0)
            ctx.DrawText(Text("Nothing to show here.", 12, FN.Muted, Reg, 300), new Point(Bounds.Width / 2 - 80, Bounds.Height / 2));
    }

    /// <summary>Raised with the hovered node (or null) and pointer position; the view draws the tooltip.</summary>
    public event Action<FileNode?, Point>? Hover;
    private void UpdateTip() => Hover?.Invoke(_hover, _hoverPt);
    private void PositionTip() => Hover?.Invoke(_hover, _hoverPt);
}

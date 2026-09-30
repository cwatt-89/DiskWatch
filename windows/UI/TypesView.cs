using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DiskWatch.Core;

namespace DiskWatch.UI;

public sealed class TypesView : ContentControl, IView
{
    private readonly AppState S;
    private string _sortKey = "total";
    private bool _asc;

    public TypesView(AppState s) { S = s; Refresh(Topic.Derived); }

    public void Refresh(Topic t)
    {
        if ((t & (Topic.Derived | Topic.Settings | Topic.Tree | Topic.Scan)) == 0 && Content != null) return;
        Content = Build();
    }

    private Control Build()
    {
        var mode = S.SizeMode;
        var cats = S.Derived.Categories;
        long total = Math.Max(1, cats.Sum(c => c.Size(mode)));

        var list = new StackPanel();
        foreach (var c in cats)
        {
            double frac = (double)c.Size(mode) / total;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,170,*,90,54,96"), Height = 32 };
            void Put(int col, Control ctl, bool right = false)
            {
                ctl.Margin = new Thickness(col == 0 ? 0 : FN.S2, 0, 0, 0);
                ctl.VerticalAlignment = VerticalAlignment.Center;
                if (right) ctl.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(ctl, col);
                g.Children.Add(ctl);
            }
            Put(0, FN.Swatch(c.Category.Brush(), 10));
            Put(1, FN.Icon(FN.IconFor(c.Category), 11));
            Put(2, FN.Data(c.Category.Title()));
            Put(3, new Bar { Fraction = frac, Fill = c.Category.Brush() });
            Put(4, FN.T(Fmt.Bytes(c.Size(mode)), 12, FN.Fg, FontWeight.SemiBold), true);
            Put(5, FN.T(Fmt.Percent(frac), 11, FN.Muted), true);
            Put(6, FN.T($"{Fmt.Count(c.Count)} files", 11, FN.Muted), true);
            var row = new Border { Child = g, BorderBrush = FN.Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            row.PointerEntered += (_, _) => row.Background = FN.Track;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            var cat = c.Category;
            row.Tapped += (_, _) => { S.SetFilter(new FilesFilter(Category: cat)); S.SetTab(Tab.Files); };
            ToolTip.SetTip(row, $"Show {cat.Title().ToLowerInvariant()} in the Files tab");
            list.Children.Add(row);
        }
        var kinds = FN.Section("By kind", FN.V(FN.S3,
                new CompositionBar { Segments = cats.Select(c => (c.Category.Brush(), (double)c.Size(mode) / total)).ToList(), BarHeight = 28 },
                list),
            $"{Fmt.Count(S.Derived.Files.Count)} files · {Fmt.Bytes(total)} · click a kind to list its files");

        const string cols = "*,150,70,86,80,56";
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), Margin = new Thickness(FN.S4, 9) };
        void H(int i, string title, string? key, bool right = false)
        {
            var h = FN.ColumnHeader(title, key != null && _sortKey == key, _asc, right, key == null ? null : () =>
            {
                if (_sortKey == key) _asc = !_asc; else { _sortKey = key; _asc = key == "ext"; }
                Content = Build();
            });
            h.Margin = new Thickness(i == 0 ? 0 : FN.S2, 0, 0, 0);
            Grid.SetColumn(h, i);
            header.Children.Add(h);
        }
        H(0, "Extension", "ext");
        H(1, "Kind", null);
        H(2, "Files", "files", true);
        H(3, "Total", "total", true);
        H(4, "Average", "average", true);
        H(5, "Share", null, true);

        var exts = S.Derived.Extensions.Take(300);
        exts = _sortKey switch
        {
            "ext" => _asc ? exts.OrderBy(e => e.Ext) : exts.OrderByDescending(e => e.Ext),
            "files" => _asc ? exts.OrderBy(e => e.Count) : exts.OrderByDescending(e => e.Count),
            "average" => _asc ? exts.OrderBy(e => e.Size(mode) / Math.Max(1, e.Count)) : exts.OrderByDescending(e => e.Size(mode) / Math.Max(1, e.Count)),
            _ => _asc ? exts.OrderBy(e => e.Size(mode)) : exts.OrderByDescending(e => e.Size(mode)),
        };
        var rows = new StackPanel();
        foreach (var e in exts)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), Height = 28, Margin = new Thickness(FN.S4, 0) };
            void Put(int col, Control ctl, bool right = false)
            {
                ctl.Margin = new Thickness(col == 0 ? 0 : FN.S2, 0, 0, 0);
                ctl.VerticalAlignment = VerticalAlignment.Center;
                if (right) ctl.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(ctl, col);
                g.Children.Add(ctl);
            }
            Put(0, FN.IconRow(FN.S1, FN.Swatch(e.Category.Brush(), 8), FN.Data(e.Ext == "(none)" ? "(no extension)" : "." + e.Ext)));
            Put(1, FN.T(e.Category.Title(), 12, FN.Muted));
            Put(2, FN.T(Fmt.Count(e.Count), 12, FN.Muted), true);
            Put(3, FN.T(Fmt.Bytes(e.Size(mode)), 12, FN.Fg, FontWeight.SemiBold), true);
            Put(4, FN.T(Fmt.Bytes(e.Count > 0 ? e.Size(mode) / e.Count : 0), 12, FN.Muted), true);
            Put(5, FN.T(Fmt.Percent((double)e.Size(mode) / total), 12, FN.Muted), true);
            var row = new Border { Child = g, BorderBrush = FN.Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
            row.PointerEntered += (_, _) => row.Background = FN.Track;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            var ext = e.Ext;
            row.Tapped += (_, _) => { S.SetFilter(new FilesFilter(Ext: ext)); S.SetTab(Tab.Files); };
            rows.Children.Add(row);
        }
        var byExt = new Border
        {
            Background = FN.Surface,
            BorderBrush = FN.Line,
            BorderThickness = new Thickness(1),
            Child = FN.V(0,
                new Border { Child = FN.SectionHeading("By extension", "Top 300 · click a row to list those files"), Padding = new Thickness(FN.S6) },
                new Border { Child = header, BorderBrush = FN.Line, BorderThickness = new Thickness(0, 0, 0, 1) },
                rows),
        };
        return FN.Scroll(new Border { Child = FN.V(FN.S5, kinds, byExt), Padding = new Thickness(FN.S5) });
    }
}

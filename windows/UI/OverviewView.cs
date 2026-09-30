using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DiskWatch.Core;

namespace DiskWatch.UI;

public sealed class OverviewView : ContentControl, IView
{
    private readonly AppState S;

    public OverviewView(AppState s) { S = s; Refresh(Topic.Derived); }

    public void Refresh(Topic t)
    {
        if ((t & (Topic.Derived | Topic.Settings | Topic.Tree | Topic.Scan | Topic.Cleanup | Topic.Volumes)) == 0 && Content != null) return;
        if (S.Root is { } root) Content = FN.Scroll(new Border { Child = Build(root), Padding = new Thickness(FN.S5) });
    }

    private Control Build(FileNode root)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
        var cards = new List<Control> { TopItems(root), Kinds(), LargestFiles(), Stale(), Cleanup() };
        if (S.Derived.Unreadable.Count > 0) cards.Add(Unreadable());
        grid.RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", (cards.Count + 1) / 2)));
        for (int i = 0; i < cards.Count; i++)
        {
            cards[i].Margin = new Thickness(i % 2 == 0 ? 0 : FN.S5 / 2, 0, i % 2 == 0 ? FN.S5 / 2 : 0, FN.S5);
            Grid.SetColumn(cards[i], i % 2);
            Grid.SetRow(cards[i], i / 2);
            grid.Children.Add(cards[i]);
        }
        return FN.V(FN.S5, Disk(root), Stats(root), grid);
    }

    private Control Disk(FileNode root)
    {
        var v = S.RootVolume;
        if (v == null) return FN.Section("Disk", FN.Caption("Volume information unavailable."));
        long scanned = root.Allocated;
        bool isVolumeScan = S.ScanPath != null && Platform.IsVolumeRoot(S.ScanPath);
        double f = v.Fraction;
        var state = f > 0.9 ? FN.PillState.Danger : f > 0.8 ? FN.PillState.Warn : FN.PillState.Ok;
        var readout = FN.Row(FN.H(FN.S2, FN.Hero($"{Math.Round(f * 100)}%"), FN.Caption("Volume capacity used", wrap: false)),
                             FN.Pill(state == FN.PillState.Ok ? "Healthy" : state == FN.PillState.Warn ? "Filling up" : "Nearly full", state));
        // This scan's share, drawn as a bright strip along the bottom of the used fill.
        var gauge = new Grid { Children = { new Gauge { Fraction = f } } };
        var strip = new Border { Height = 4, Background = FN.Fg, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) };
        gauge.Children.Add(strip);
        gauge.SizeChanged += (_, e) => strip.Width = e.NewSize.Width * Math.Min(f, (double)scanned / Math.Max(1, v.Total));
        var labels = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        var mid = FN.T("ceiling 90%", 11, FN.Muted);
        var hundred = FN.T("100%", 11, FN.Muted);
        hundred.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(mid, 1);
        Grid.SetColumn(hundred, 2);
        labels.Children.Add(FN.T("0%", 11, FN.Muted));
        labels.Children.Add(mid);
        labels.Children.Add(hundred);
        Control Legend(IBrush c, string label, string value, IBrush? stroke = null) => FN.H(6, FN.Swatch(c, 9, stroke ?? FN.Hair), FN.Label(label), FN.Data(value));
        var legend = FN.H(FN.S5, Legend(FN.Fg, "This scan", Fmt.Bytes(scanned)), Legend(Gauge.StateBrush(f, 0.9), "Used", Fmt.Bytes(v.Used)), Legend(FN.Track, "Free", Fmt.Bytes(v.Available)));
        var body = FN.V(FN.S3, readout, gauge, labels, legend);
        if (isVolumeScan && v.Used > scanned)
            body.Children.Add(FN.Caption($"{Fmt.Bytes(v.Used - scanned)} in use isn't visible as files: restore points (shadow copies), NTFS metadata, the page/hibernation files' reserved space, or folders DiskWatch couldn't read."));
        return FN.Section("Disk", body, $"{v.Name} · {Fmt.Bytes(v.Total)} volume");
    }

    private Control Stats(FileNode root)
    {
        var files = S.Derived.Files;
        long avg = files.Count == 0 ? 0 : root.Logical / files.Count;
        var biggest = root.Children.MaxBy(c => c.Size(S.SizeMode));
        var row1 = FN.TileRow(FN.StatTile("Size on disk", Fmt.Bytes(root.Allocated)), FN.StatTile("File size", Fmt.Bytes(root.Logical)),
                              FN.StatTile("Files", Fmt.Count(root.FileCount)), FN.StatTile("Folders", Fmt.Count(root.DirCount)));
        var row2 = FN.TileRow(FN.StatTile("Average file", Fmt.Bytes(avg)),
                              FN.StatTile("Largest item", biggest != null ? Fmt.Bytes(biggest.Size(S.SizeMode)) : "—", biggest?.Name),
                              FN.StatTile("Scan time", Fmt.Duration(S.ScanDuration), S.ScanWasCancelled ? "stopped early" : null),
                              FN.StatTile("Unreadable", Fmt.Count(S.Derived.Unreadable.Count), valueColor: S.Derived.Unreadable.Count == 0 ? FN.Fg : FN.Warn));
        return FN.V(FN.S5, row1, row2);
    }

    private Control Clickable(Control content, Action onClick, FileNode? menuFor = null)
    {
        var b = new Border { Child = content, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        b.Tapped += (_, _) => onClick();
        if (menuFor != null) b.ContextRequested += (_, e) => { ItemMenu.Build(S, new() { menuFor }).Open(b); e.Handled = true; };
        return b;
    }

    private Control TopItems(FileNode root)
    {
        var mode = S.SizeMode;
        var top = root.Children.OrderByDescending(c => c.Size(mode)).Take(10).ToList();
        double max = Math.Max(1, top.FirstOrDefault()?.Size(mode) ?? 1);
        var list = new StackPanel { Spacing = FN.S2 };
        foreach (var n in top)
        {
            var line = FN.Row(FN.IconRow(FN.S1, FN.Icon(FN.IconFor(n), 10), FN.Data(n.Name)), FN.Data(Fmt.Bytes(n.Size(mode))),
                              new Border { Width = 50, Child = FN.T(Fmt.Percent(n.FractionOfParent(mode)), 11, FN.Muted) });
            ((Control)line.Children[^1]).HorizontalAlignment = HorizontalAlignment.Right;
            var bar = new Bar { Fraction = n.Size(mode) / max, Fill = n.IsDirectory ? FN.Accent : FileCategoryExt.Of(n).Brush(), BarHeight = 4 };
            list.Children.Add(Clickable(FN.V(5, line, bar), () => S.ShowInTree(n), n));
        }
        return FN.Section("Biggest items", list, "Top level of this scan", FN.Link("Open Tree", () => S.SetTab(Tab.Tree)));
    }

    private Control Kinds()
    {
        var mode = S.SizeMode;
        var cats = S.Derived.Categories;
        long total = Math.Max(1, cats.Sum(c => c.Size(mode)));
        var list = new StackPanel { Spacing = 6 };
        foreach (var c in cats.Take(8))
            list.Children.Add(FN.Row(FN.IconRow(FN.S1, FN.Swatch(c.Category.Brush()), FN.Data(c.Category.Title())), FN.Data(Fmt.Bytes(c.Size(mode))),
                                     new Border { Width = 50, Child = new TextBlock { Text = Fmt.Percent((double)c.Size(mode) / total), FontFamily = FN.Mono, FontSize = 11, Foreground = FN.Muted, HorizontalAlignment = HorizontalAlignment.Right } }));
        var bar = new CompositionBar { Segments = cats.Select(c => (c.Category.Brush(), (double)c.Size(mode) / total)).ToList() };
        return FN.Section("What's using space", FN.V(FN.S3, bar, list), "By kind of file", FN.Link("Open Types", () => S.SetTab(Tab.Types)));
    }

    private Control FileLine(FileNode f, string detail)
    {
        var line = FN.Row(FN.IconRow(FN.S1, FN.Icon(FN.IconFor(f), 10), FN.V(2, FN.Data(f.Name), FN.T(Fmt.Abbreviate(f.Parent?.FullPath ?? ""), 10, FN.Muted))), FN.Data(detail));
        return Clickable(new Border { Child = line, Padding = new Thickness(0, 7), BorderBrush = FN.Line, BorderThickness = new Thickness(0, 0, 0, 1) }, () => S.ShowInTree(f), f);
    }

    private Control LargestFiles()
    {
        var list = new StackPanel();
        foreach (var f in S.Derived.Files.Take(8)) list.Children.Add(FileLine(f, Fmt.Bytes(f.Size(S.SizeMode))));
        return FN.Section("Largest files", list, "Anywhere in this scan", FN.Link("All Files", () => { S.SetFilter(new FilesFilter()); S.SetTab(Tab.Files); }));
    }

    private Control Stale()
    {
        long cutoff = DateTime.UtcNow.AddDays(-365).Ticks;
        var items = S.Derived.Files.Where(f => f.ModifiedTicks < cutoff && f.Size(S.SizeMode) >= 50_000_000 && !f.InProgramFiles).Take(6).ToList();
        Control body;
        if (items.Count == 0) body = FN.Caption("No large files older than a year.");
        else
        {
            var list = new StackPanel();
            foreach (var f in items) list.Children.Add(FileLine(f, Fmt.Bytes(f.Size(S.SizeMode)) + " · " + Fmt.ShortDate(f.ModifiedTicks)));
            body = list;
        }
        return FN.Section("Untouched", body, "50 MB+ and not modified for a year",
            FN.Link("Show More", () => { S.SetFilter(new FilesFilter(MinSize: 50_000_000, OlderThanDays: 365)); S.SetTab(Tab.Files); }));
    }

    private Control Cleanup()
    {
        var c = S.Cleanup;
        Control body;
        if (c.HasScanned)
        {
            var list = FN.V(FN.S3, FN.H(FN.S1, FN.Value(Fmt.Bytes(c.TotalFound), FN.Accent), FN.Caption(c.IsMeasuring ? "found so far…" : "reclaimable", wrap: false)));
            foreach (var item in c.VisibleItems.OrderByDescending(i => c.Get(i.Id)?.Size ?? 0).Take(4))
                list.Children.Add(FN.Row(FN.Data(item.Title), FN.Pill(item.Safety.Short(), item.Safety.Pill()),
                    new Border { Width = 80, Child = new TextBlock { Text = Fmt.Bytes(c.Get(item.Id)?.Size ?? 0), FontFamily = FN.Mono, FontSize = 12, Foreground = FN.Fg, HorizontalAlignment = HorizontalAlignment.Right } }));
            body = list;
        }
        else body = FN.Caption("See how much space temp files, caches, logs, developer files and old installers are using.");
        return FN.Section("Cleanup", body, "Caches, logs & leftovers",
            FN.Link(c.HasScanned ? "Open Cleanup" : "Analyze", () => { if (!c.HasScanned) c.ScanAll(); S.SetTab(Tab.Cleanup); }));
    }

    private Control Unreadable()
    {
        var list = new StackPanel { Spacing = FN.S1 };
        list.Children.Add(FN.Caption(Platform.IsAdmin
            ? "Even administrators are denied some folders (other users' private data, System Volume Information). They aren't counted."
            : "Run DiskWatch as administrator to include these."));
        foreach (var n in S.Derived.Unreadable.Take(6)) list.Children.Add(FN.T(Fmt.Abbreviate(n.FullPath), 11, FN.Muted));
        return FN.Section("Unreadable", list, $"{S.Derived.Unreadable.Count} folders weren't counted");
    }
}

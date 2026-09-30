using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DiskWatch.Core;

namespace DiskWatch.UI;

public sealed class DuplicatesView : DockPanel, IView
{
    private readonly AppState S;
    private readonly ContentControl _body = new(), _footer = new();

    public DuplicatesView(AppState s)
    {
        S = s;
        SetDock(_footer, Dock.Bottom);
        Children.Add(_footer);
        Children.Add(_body);
        Refresh(Topic.Duplicates);
    }

    public void Refresh(Topic t)
    {
        if ((t & (Topic.Duplicates | Topic.Scan | Topic.Derived)) == 0 && _body.Content != null) return;
        var m = S.Duplicates;
        _body.Content = FN.Scroll(new Border { Child = Build(), Padding = new Thickness(FN.S5) });
        _footer.Content = m.Groups.Count > 0 && !m.IsRunning ? FN.V(0, FN.Rule(), Footer()) : null;
    }

    private Control Build()
    {
        var m = S.Duplicates;
        var picker = FN.MenuPicker("Min size", new (long, string)[] { (100_000, "100 KB"), (1_000_000, "1 MB"), (10_000_000, "10 MB"), (100_000_000, "100 MB") },
            m.MinSize, v => { m.MinSize = v; Refresh(Topic.Duplicates); });
        var search = FN.Button(m.HasRun ? "Search Again" : "Search", FNButton.Kind.Primary, () => m.Run(S.Derived.Files), "search");
        search.IsEnabled = !m.IsRunning && S.Root != null;
        Control tiles = m.Groups.Count > 0
            ? FN.TileRow(FN.StatTile("Reclaimable", Fmt.Bytes(m.TotalWasted), valueColor: FN.Accent), FN.StatTile("Groups", Fmt.Count(m.Groups.Count)),
                         FN.StatTile("Duplicate files", Fmt.Count(m.Groups.Sum(g => g.Files.Count - 1))), FN.StatTile("Marked", Fmt.Bytes(m.MarkedSize)))
            : FN.Caption(m.HasRun && !m.IsRunning ? m.Status : "Identical files anywhere in the current scan.");
        var stack = FN.V(FN.S5, FN.Section("Duplicates", tiles, "Same-size files compared by SHA-256 — only byte-for-byte copies.", FN.H(FN.S2, picker, search)));

        if (m.IsRunning)
        {
            stack.Children.Add(FN.Section(null, FN.V(FN.S2,
                FN.Label("Comparing"),
                new Gauge { Fraction = m.Progress, Threshold = null, BarHeight = 10, FillOverride = FN.Accent },
                FN.T(m.Status, 11, FN.Muted),
                new Border { Child = FN.Button("Stop", FNButton.Kind.Danger, m.Stop, compact: true), HorizontalAlignment = HorizontalAlignment.Left })));
            return stack;
        }
        foreach (var g in m.Groups.Take(200)) stack.Children.Add(Group(g));
        if (m.Groups.Count > 200) stack.Children.Add(FN.Caption("Showing the 200 groups that waste the most space."));
        return stack;
    }

    private Control Group(DuplicatesModel.Group g)
    {
        var inner = new StackPanel();
        inner.Children.Add(new Border
        {
            Child = FN.Row(FN.IconRow(FN.S1, FN.Icon(FN.IconFor(g.Files[0]), 10), FN.T($"{g.Files.Count} × {Fmt.Bytes(g.Size)}", 11, FN.Muted), FN.T(g.Files[0].Name, 12, FN.Fg, FontWeight.SemiBold)),
                           FN.Pill($"{Fmt.Bytes(g.Wasted)} wasted", FN.PillState.Warn)),
            Padding = new Thickness(FN.S4, 10),
            BorderBrush = FN.Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
        });
        foreach (var f in g.Files) inner.Children.Add(FileRow(f));
        return new Border { Background = FN.Surface, BorderBrush = FN.Line, BorderThickness = new Thickness(1), Child = inner };
    }

    private Control FileRow(FileNode f)
    {
        var m = S.Duplicates;
        var verdict = SafetyClassifier.Classify(f);
        bool marked = m.Marked.Contains(f);
        var check = FN.Checkbox(marked, _ => { m.Toggle(f); }, enabled: verdict.Level != SafetyLevel.Protected);
        var text = FN.V(2, FN.T(Fmt.Abbreviate(f.FullPath), 11, FN.Fg), FN.T("Modified " + Fmt.Date(f.ModifiedTicks), 10, FN.Muted));
        var row = FN.Row(FN.IconRow(FN.S2, check, text), FN.Pill(verdict.Level.Short(), verdict.Level.Pill()),
            FN.IconButton("open", FNButton.Kind.Ghost, () => Platform.Reveal(new[] { f.FullPath }), ItemMenu.RevealName),
            FN.IconButton("eye", FNButton.Kind.Ghost, () => Platform.Properties(f.FullPath), ItemMenu.PropertiesName));
        var b = new Border { Child = row, Padding = new Thickness(FN.S4, 7), BorderBrush = FN.Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = marked ? FN.Track : Brushes.Transparent };
        b.ContextRequested += (_, e) => { ItemMenu.Build(S, new() { f }).Open(b); e.Handled = true; };
        return b;
    }

    private Control Footer()
    {
        var m = S.Duplicates;
        var auto = FN.Button("Auto-select", FNButton.Kind.Secondary, () => { }, "down", compact: true);
        var flyout = new MenuFlyout();
        foreach (var (title, keep) in new[] { ("Keep newest in each group", DuplicatesModel.Keep.Newest), ("Keep oldest in each group", DuplicatesModel.Keep.Oldest), ("Keep shortest path in each group", DuplicatesModel.Keep.ShortestPath) })
        {
            var mi = new MenuItem { Header = title };
            mi.Click += (_, _) => m.AutoMark(keep);
            flyout.Items.Add(mi);
        }
        auto.Click += () => flyout.ShowAt(auto);
        bool warn = m.Status.StartsWith("Keep");
        var note = FN.T(warn ? m.Status : "NTFS hard links share storage — freed space can be less than shown.", 10, warn ? FN.Warn : FN.Muted);
        var trash = FN.Button($"Move to {ItemMenu.RecycleName}…", FNButton.Kind.Danger, () => S.RequestDelete(m.MarkedNodes, false, "Duplicates"), "trash");
        trash.IsEnabled = m.Marked.Count > 0;
        var left = FN.H(FN.S2, auto, FN.Button("Clear", FNButton.Kind.Secondary, () => { m.Marked.Clear(); Refresh(Topic.Duplicates); }, compact: true), note);
        return new Border { Child = FN.Row(left, FN.T($"{m.Marked.Count} marked · {Fmt.Bytes(m.MarkedSize)}", 11, FN.Fg, FontWeight.SemiBold), trash), Padding = new Thickness(FN.S5, FN.S2) };
    }
}

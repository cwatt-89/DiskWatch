using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DiskWatch.Core;

namespace DiskWatch.UI;

public sealed class InspectorView : ContentControl
{
    private readonly AppState S;

    public InspectorView(AppState s)
    {
        S = s;
        Control body = S.Selection.Count > 1 ? Multi(S.Selection)
            : S.Focused is { } n ? Single(n)
            : FN.Caption("Select a file or folder to see details.");
        Content = new ScrollViewer { Content = new Border { Child = body, Padding = new Thickness(FN.S4) } };
    }

    private Control Single(FileNode n)
    {
        var mode = S.SizeMode;
        var verdict = SafetyClassifier.Classify(n);
        var stack = new StackPanel { Spacing = FN.S5 };
        stack.Children.Add(FN.V(FN.S1,
            FN.Label(n.IsDirectory ? (n.IsLink ? "Link" : "Folder") : FileCategoryExt.Of(n).Title()),
            FN.IconRow(FN.S1, FN.Icon(FN.IconFor(n), 12), FN.T(n.DisplayName, 14, FN.Fg, FontWeight.SemiBold, wrap: true))));

        var size = FN.V(10, FN.Label(mode.Title()), FN.Value(Fmt.Bytes(n.Size(mode))));
        if (S.Root is { } root && !ReferenceEquals(root, n) && root.Size(mode) > 0)
        {
            double f = (double)n.Size(mode) / root.Size(mode);
            size.Children.Add(new Gauge { Fraction = f, Threshold = null, BarHeight = 8, FillOverride = FN.Accent });
            size.Children.Add(FN.Caption($"{Fmt.Percent(f)} of scan · {Fmt.Percent(n.FractionOfParent(mode))} of parent"));
        }
        stack.Children.Add(size);

        var safety = FN.V(FN.S1, FN.Row(FN.Label("Safety"), FN.Pill(verdict.Level.Title(), verdict.Level.Pill())));
        foreach (var r in verdict.Reasons) safety.Children.Add(FN.T(r, 11, FN.Muted, wrap: true));
        stack.Children.Add(new Border
        {
            Child = safety,
            Padding = new Thickness(FN.S2),
            BorderBrush = verdict.Level == SafetyLevel.Caution ? FN.Hair : verdict.Level.Tint(),
            BorderThickness = new Thickness(1),
        });

        var details = Section("Details");
        details.Children.Add(Row("Size on disk", Fmt.Bytes(n.Allocated)));
        details.Children.Add(Row("File size", Fmt.Bytes(n.Logical)));
        if (n.IsDirectory)
        {
            details.Children.Add(Row("Files", Fmt.Count(n.FileCount)));
            details.Children.Add(Row("Folders", Fmt.Count(n.DirCount)));
        }
        details.Children.Add(Row("Modified", Fmt.Date(n.ModifiedTicks)));
        details.Children.Add(Row("Created", Fmt.Date(n.CreatedTicks)));
        details.Children.Add(Row("Last opened", Fmt.Date(n.AccessedTicks)));
        details.Children.Add(Row("Attributes", Fmt.Attributes(n.Attributes)));
        if (n.Unreadable) details.Children.Add(Row("Status", "Not readable", FN.Warn));
        if (n.Excluded) details.Children.Add(Row("Status", "Skipped", FN.Muted));
        stack.Children.Add(details);

        var loc = Section("Location");
        loc.Children.Add(new SelectableTextBlock { Text = n.FullPath, FontFamily = FN.Mono, FontSize = 11, Foreground = FN.Fg, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(loc);

        if (n.IsDirectory && n.Children.Count > 0)
        {
            var inside = Section("Largest inside");
            var top = n.Children.OrderByDescending(c => c.Size(mode)).Take(8).ToList();
            double max = Math.Max(1, top[0].Size(mode));
            foreach (var c in top)
            {
                var b = new Border
                {
                    Child = FN.V(4, FN.Row(FN.T(c.Name, 11, FN.Fg), FN.T(Fmt.Bytes(c.Size(mode)), 11, FN.Muted)), new Bar { Fraction = c.Size(mode) / max, BarHeight = 3 }),
                    Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                b.Tapped += (_, _) => S.Select(c);
                inside.Children.Add(b);
            }
            stack.Children.Add(inside);
        }
        stack.Children.Add(Actions(new() { n }, verdict.Level));
        return stack;
    }

    private Control Multi(List<FileNode> nodes)
    {
        var mode = S.SizeMode;
        long total = nodes.Where(n => !nodes.Any(n.IsDescendantOf)).Sum(n => n.Size(mode));
        var levels = nodes.Select(n => SafetyClassifier.Classify(n).Level).ToList();
        var stack = new StackPanel { Spacing = FN.S5 };
        stack.Children.Add(FN.V(FN.S1, FN.Label($"{nodes.Count} items selected"), FN.Value(Fmt.Bytes(total))));
        var safety = Section("Safety");
        foreach (var lvl in Enum.GetValues<SafetyLevel>().Reverse())
        {
            int c = levels.Count(l => l == lvl);
            if (c > 0) safety.Children.Add(FN.Row(FN.Pill(lvl.Short(), lvl.Pill()), FN.Data(c.ToString())));
        }
        stack.Children.Add(safety);
        stack.Children.Add(Actions(nodes, levels.Max()));
        return stack;
    }

    private Control Actions(List<FileNode> nodes, SafetyLevel worst)
    {
        bool blocked = worst == SafetyLevel.Protected && nodes.Count == 1;
        var s = Section("Actions");
        Control Pair(FNButton a, FNButton? b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(b == null ? "*" : "*,*") };
            a.HorizontalAlignment = HorizontalAlignment.Stretch;
            g.Children.Add(a);
            if (b != null)
            {
                b.Margin = new Thickness(FN.S1, 0, 0, 0);
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetColumn(b, 1);
                g.Children.Add(b);
            }
            return g;
        }
        var first = nodes[0];
        s.Children.Add(Pair(FN.Button(Platform.IsWindows ? "Show" : "Reveal", FNButton.Kind.Secondary, () => Platform.Reveal(nodes.Select(n => n.FullPath)), compact: true),
                            nodes.Count == 1 ? FN.Button(Platform.IsWindows ? "Properties" : "Preview", FNButton.Kind.Secondary, () => Platform.Properties(first.FullPath), compact: true) : null));
        if (nodes.Count == 1 && first.IsDirectory)
            s.Children.Add(Pair(FN.Button("Rescan", FNButton.Kind.Secondary, () => S.Rescan(first), compact: true),
                                FN.Button("Treemap", FNButton.Kind.Secondary, () => S.ShowInTreemap(first), compact: true)));
        var trash = FN.Button($"Move to {ItemMenu.RecycleName}…", FNButton.Kind.Danger, () => S.RequestDelete(nodes, false), "trash");
        var del = FN.Button("Delete Permanently…", FNButton.Kind.Danger, () => S.RequestDelete(nodes, true), "x");
        trash.IsEnabled = del.IsEnabled = !blocked;
        trash.Margin = new Thickness(0, FN.S1, 0, 0);
        s.Children.Add(Pair(trash, null));
        s.Children.Add(Pair(del, null));
        if (blocked) s.Children.Add(FN.Caption("DiskWatch won't remove protected items."));
        return s;
    }

    private static StackPanel Section(string title)
    {
        var s = new StackPanel { Spacing = FN.S1 };
        s.Children.Add(new Border { Child = FN.Label(title), BorderBrush = FN.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 4) });
        return s;
    }

    private static Control Row(string k, string v, IBrush? color = null)
    {
        var val = FN.T(v, 11, color ?? FN.Fg);
        val.HorizontalAlignment = HorizontalAlignment.Right;
        return FN.Row(FN.T(k, 11, FN.Muted), val);
    }
}

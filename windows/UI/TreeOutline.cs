using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DiskWatch.Core;

namespace DiskWatch.UI;

/// <summary>The shared right-click menu used by the tree, treemap and file lists.</summary>
public static class ItemMenu
{
    public static string RecycleName => Platform.IsWindows ? "Recycle Bin" : "Trash";
    public static string RevealName => Platform.IsWindows ? "Show in Explorer" : "Reveal in Finder";
    public static string PropertiesName => Platform.IsWindows ? "Properties" : "Quick Look";

    public static TopLevel? Top => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static ContextMenu Build(AppState s, List<FileNode> nodes)
    {
        var menu = new ContextMenu();
        if (nodes.Count == 0) return menu;
        var first = nodes[0];
        bool single = nodes.Count == 1;
        void Add(string title, Action act, bool enabled = true)
        {
            var mi = new MenuItem { Header = title, IsEnabled = enabled };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
        Add(RevealName, () => Platform.Reveal(nodes.Select(n => n.FullPath)));
        if (single)
        {
            Add("Open", () => Platform.Open(first.FullPath));
            Add(PropertiesName, () => Platform.Properties(first.FullPath));
        }
        Add(single ? "Copy Path" : "Copy Paths", () => Top?.Clipboard?.SetTextAsync(string.Join(Environment.NewLine, nodes.Select(n => n.FullPath))));
        menu.Items.Add(new Separator());
        if (single && first.IsDirectory)
        {
            Add("Scan This Folder", () => s.Scan(first.FullPath));
            Add("Rescan This Folder", () => s.Rescan(first));
        }
        if (single)
        {
            Add("Show in Tree", () => s.ShowInTree(first));
            Add("Show in Treemap", () => s.ShowInTreemap(first));
        }
        if (single && first.IsDirectory) Add("Exclude from Future Scans", () => s.ExcludeFromScans(first.FullPath));
        menu.Items.Add(new Separator());
        var levels = nodes.Select(n => SafetyClassifier.Classify(n).Level).ToList();
        var worst = levels.Max();
        bool allBlocked = levels.All(l => l == SafetyLevel.Protected);
        menu.Items.Add(new MenuItem { Header = "Safety: " + worst.Title(), IsEnabled = false, Icon = FN.Icon("warn", 10, worst.Tint()) });
        Add($"Move to {RecycleName}…", () => s.RequestDelete(nodes, permanent: false), !allBlocked);
        Add("Delete Permanently…", () => s.RequestDelete(nodes, permanent: true), !allBlocked);
        return menu;
    }
}

/// <summary>TreeSize-style outline, rendered as a virtualized flat list of visible rows.</summary>
public sealed class TreeOutline : DockPanel, IView
{
    private sealed class Row
    {
        public required FileNode Node;
        public required int Depth;
    }

    public const string Columns = "*,100,112,86,70,64,112,96";
    private readonly AppState S;
    private readonly AvaloniaList<Row> _rows = new();
    private readonly HashSet<FileNode> _expanded = new();
    private readonly ListBox _list;
    private readonly ContentControl _header = new();
    private string _sortKey = "size";
    private bool _asc;
    private bool _syncing;
    private int _builtVersion = -1, _builtSort = -1;

    public TreeOutline(AppState s)
    {
        S = s;
        _list = new ListBox
        {
            ItemsSource = _rows,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<Row>((r, _) => BuildRow(r)),
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            S.SetSelection(_list.SelectedItems!.OfType<Row>().Select(r => r.Node).ToList());
        };
        _list.DoubleTapped += (_, e) =>
        {
            if (_list.SelectedItem is not Row r) return;
            if (r.Node.IsDirectory && r.Node.Children.Count > 0) Toggle(r); else Platform.Properties(r.Node.FullPath);
        };
        _list.KeyDown += OnKey;
        _list.ContextRequested += (_, e) =>
        {
            var nodes = _list.SelectedItems!.OfType<Row>().Select(r => r.Node).ToList();
            if (nodes.Count == 0) return;
            ItemMenu.Build(S, nodes).Open(_list);
            e.Handled = true;
        };
        var headerRule = FN.Rule();
        SetDock(_header, Dock.Top);
        SetDock(headerRule, Dock.Top);
        Children.Add(_header);
        Children.Add(headerRule);
        Children.Add(_list);
        S.RevealHandler = Reveal;
        if (S.Root != null) _expanded.Add(S.Root);
        Refresh(Topic.Tree | Topic.Selection);
    }

    public void Refresh(Topic t)
    {
        if (S.RevealHandler != Reveal && IsVisible) S.RevealHandler = Reveal;
        if (_builtVersion != S.TreeVersion || _builtSort != S.SortStamp || t.HasFlag(Topic.Settings))
        {
            _builtVersion = S.TreeVersion;
            _builtSort = S.SortStamp;
            _header.Content = Header();
            RebuildRows();
        }
        if (t.HasFlag(Topic.Selection)) SyncSelection();
    }

    private Control Header()
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Margin = new Thickness(10, 9, 10, 9) };
        void Col(int i, string title, string key, bool right = false)
        {
            var h = FN.ColumnHeader(title, _sortKey == key, _asc, right, () =>
            {
                if (_sortKey == key) _asc = !_asc; else { _sortKey = key; _asc = key == "name"; }
                S.SortStamp++;
                Refresh(Topic.Tree);
            });
            h.Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0);
            Grid.SetColumn(h, i);
            g.Children.Add(h);
        }
        Col(0, "Name", "name");
        Col(1, S.SizeMode.Title(), "size", true);
        Col(2, "Share of Parent", "share");
        Col(3, S.SizeMode.Other().Title(), "other", true);
        Col(4, "Files", "files", true);
        Col(5, "Folders", "folders", true);
        Col(6, "Modified", "modified");
        Col(7, "Safety", "safety");
        return g;
    }

    private List<FileNode> Sorted(FileNode n)
    {
        if (n.SortStamp != S.SortStamp)
        {
            var mode = S.SizeMode;
            int Dir(int c) => _asc ? c : -c;
            Comparison<FileNode> cmp = _sortKey switch
            {
                "name" => (a, b) => Dir(string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase)),
                "other" => (a, b) => Dir(a.Size(mode.Other()).CompareTo(b.Size(mode.Other()))),
                "files" => (a, b) => Dir((a.IsDirectory ? a.FileCount : 1).CompareTo(b.IsDirectory ? b.FileCount : 1)),
                "folders" => (a, b) => Dir(a.DirCount.CompareTo(b.DirCount)),
                "modified" => (a, b) => Dir(a.ModifiedTicks.CompareTo(b.ModifiedTicks)),
                "safety" => (a, b) => Dir(SafetyClassifier.Classify(a).Level.CompareTo(SafetyClassifier.Classify(b).Level)),
                _ => (a, b) => Dir(a.Size(mode).CompareTo(b.Size(mode))),
            };
            n.Children.Sort(cmp);
            n.SortStamp = S.SortStamp;
        }
        return n.Children;
    }

    private void Visit(FileNode n, int depth, List<Row> into)
    {
        into.Add(new Row { Node = n, Depth = depth });
        if (n.IsDirectory && _expanded.Contains(n))
            foreach (var c in Sorted(n)) Visit(c, depth + 1, into);
    }

    private void RebuildRows()
    {
        var selected = S.Selection.ToHashSet();
        _syncing = true;
        _rows.Clear();
        if (S.Root != null)
        {
            _expanded.RemoveWhere(n => n.Parent == null && !ReferenceEquals(n, S.Root));
            _expanded.Add(S.Root);
            var list = new List<Row>();
            Visit(S.Root, 0, list);
            _rows.AddRange(list);
            foreach (var r in _rows.Where(r => selected.Contains(r.Node))) _list.SelectedItems!.Add(r);
        }
        _syncing = false;
    }

    private void Toggle(Row r)
    {
        int idx = _rows.IndexOf(r);
        if (idx < 0) return;
        if (_expanded.Remove(r.Node))
        {
            int count = 0;
            while (idx + 1 + count < _rows.Count && _rows[idx + 1 + count].Depth > r.Depth) count++;
            _rows.RemoveRange(idx + 1, count);
        }
        else
        {
            _expanded.Add(r.Node);
            var list = new List<Row>();
            foreach (var c in Sorted(r.Node)) Visit(c, r.Depth + 1, list);
            _rows.InsertRange(idx + 1, list);
        }
        // Replace the row so its chevron redraws.
        _syncing = true;
        bool wasSelected = _list.SelectedItems!.Contains(r);
        var fresh = new Row { Node = r.Node, Depth = r.Depth };
        _rows[idx] = fresh;
        if (wasSelected) _list.SelectedItems.Add(fresh);
        _syncing = false;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_list.SelectedItem is not Row r) return;
        switch (e.Key)
        {
            case Key.Right when r.Node.IsDirectory && !_expanded.Contains(r.Node) && r.Node.Children.Count > 0: Toggle(_rows[_rows.IndexOf(r)]); break;
            case Key.Left when _expanded.Contains(r.Node): Toggle(r); break;
            case Key.Left when r.Node.Parent != null: Reveal(r.Node.Parent); S.SetSelection(new() { r.Node.Parent }); break;
            case Key.Enter: Platform.Properties(r.Node.FullPath); break;
            default: return;
        }
        e.Handled = true;
    }

    private void SyncSelection()
    {
        var want = S.Selection.ToHashSet();
        var have = _list.SelectedItems!.OfType<Row>().Select(r => r.Node).ToHashSet();
        if (want.SetEquals(have)) return;
        _syncing = true;
        _list.SelectedItems.Clear();
        foreach (var r in _rows.Where(r => want.Contains(r.Node))) _list.SelectedItems.Add(r);
        _syncing = false;
    }

    public void Reveal(FileNode node)
    {
        foreach (var a in node.Ancestors.Reverse())
        {
            if (_expanded.Contains(a)) continue;
            var row = _rows.FirstOrDefault(r => ReferenceEquals(r.Node, a));
            if (row != null) Toggle(row); else _expanded.Add(a);
        }
        var target = _rows.FirstOrDefault(r => ReferenceEquals(r.Node, node));
        if (target == null) return;
        _syncing = true;
        _list.SelectedItems!.Clear();
        _list.SelectedItems.Add(target);
        _syncing = false;
        _list.ScrollIntoView(target);
    }

    private Control BuildRow(Row r)
    {
        var n = r.Node;
        var mode = S.SizeMode;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 26, Margin = new Thickness(10, 0) };
        bool expandable = n.IsDirectory && n.Children.Count > 0;
        bool isRoot = n.Parent == null;
        bool dim = n.Excluded || n.Unreadable;

        var chevron = new Border { Width = 14, Background = Brushes.Transparent, Cursor = expandable ? new Cursor(StandardCursorType.Hand) : null };
        if (expandable)
        {
            chevron.Child = FN.Icon(_expanded.Contains(n) ? "down" : "right", 8, cls: "muted");
            chevron.PointerPressed += (_, e) => { e.Handled = true; Toggle(r); };
        }
        var name = FN.T(n.DisplayName + (n.Excluded ? "  [skipped]" : "") + (n.IsLink && n.IsDirectory ? "  [link]" : ""), 12,
                        weight: isRoot ? FontWeight.SemiBold : FontWeight.Normal, cls: dim ? "muted" : "fg");
        Control nameText = name;
        if (n.Unreadable)
        {
            var lockIcon = FN.Icon("lock", 10, cls: "warn");
            ToolTip.SetTip(lockIcon, "Could not be read — access denied.");
            var wrap = new DockPanel();
            DockPanel.SetDock(lockIcon, Dock.Right);
            lockIcon.Margin = new Thickness(6, 0, 0, 0);
            wrap.Children.Add(lockIcon);
            wrap.Children.Add(name);
            nameText = wrap;
        }
        var nameCell = FN.IconRow(6, chevron, FN.Icon(FN.IconFor(n), 12, cls: isRoot ? "accent" : "muted"), nameText);
        nameCell.Margin = new Thickness(r.Depth * 16, 0, 0, 0);
        ToolTip.SetTip(nameCell, n.FullPath);
        Put(g, 0, nameCell);

        Put(g, 1, Right(FN.T(Fmt.Bytes(n.Size(mode)), 12, weight: FontWeight.SemiBold, cls: "fg")));
        var share = new Grid { ColumnDefinitions = new ColumnDefinitions("*,46") };
        share.Children.Add(new Bar { Fraction = isRoot ? 1 : n.FractionOfParent(mode), Fill = dim ? FN.Hair : FN.Accent, VerticalAlignment = VerticalAlignment.Center });
        var pct = Right(FN.T(isRoot ? "" : Fmt.Percent(n.FractionOfParent(mode)), 11, cls: "muted"));
        Grid.SetColumn(pct, 1);
        share.Children.Add(pct);
        Put(g, 2, share);
        Put(g, 3, Right(FN.T(Fmt.Bytes(n.Size(mode.Other())), 12, cls: "muted")));
        Put(g, 4, Right(FN.T(n.IsDirectory ? Fmt.Count(n.FileCount) : "", 12, cls: "muted")));
        Put(g, 5, Right(FN.T(n.IsDirectory ? Fmt.Count(n.DirCount) : "", 12, cls: "muted")));
        Put(g, 6, FN.T(Fmt.ShortDateTime(n.ModifiedTicks), 12, cls: "muted"));
        var verdict = SafetyClassifier.Classify(n);
        var pill = FN.Pill(verdict.Level.Short(), verdict.Level.Pill());
        ToolTip.SetTip(pill, verdict.Level.Title() + "\n" + string.Join("\n", verdict.Reasons));
        Put(g, 7, pill);
        return new Border { Child = g, BorderBrush = FN.Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent };
    }

    private static Control Right(TextBlock tb) { tb.HorizontalAlignment = HorizontalAlignment.Right; return tb; }

    private static void Put(Grid g, int col, Control c)
    {
        if (col > 0) c.Margin = new Thickness(10, 0, 0, 0);
        c.VerticalAlignment = VerticalAlignment.Center;
        c.ClipToBounds = true;
        Grid.SetColumn(c, col);
        g.Children.Add(c);
    }
}

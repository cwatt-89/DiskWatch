using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using DiskWatch.Core;

namespace DiskWatch.UI;

/// <summary>Filterable, sortable list of every file in the scan (largest first).</summary>
public sealed class FilesView : DockPanel, IView
{
    private const string Columns = "2*,98,74,150,86,*";
    private readonly AppState S;
    private readonly ListBox _list;
    private readonly ContentControl _filters = new(), _header = new(), _footer = new();
    private List<FileNode> _results = new();
    private int _matchCount;
    private long _matchSize;
    private string _sortKey = "size";
    private bool _asc;
    private bool _computing;
    private FilesFilter? _lastFilter;
    private int _lastCount = -1, _lastVersion = -1;
    private SizeMode _lastMode;
    private CancellationTokenSource? _cts;
    private TextBox? _text;

    public FilesView(AppState s)
    {
        S = s;
        _list = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<FileNode>((n, _) => BuildRow(n)),
        };
        _list.SelectionChanged += (_, _) =>
        {
            S.SetSelection(_list.SelectedItems!.OfType<FileNode>().ToList());
            _footer.Content = Footer();
        };
        _list.DoubleTapped += (_, _) => { if (_list.SelectedItem is FileNode n) Platform.Properties(n.FullPath); };
        _list.ContextRequested += (_, e) =>
        {
            var nodes = _list.SelectedItems!.OfType<FileNode>().ToList();
            if (nodes.Count == 0) return;
            ItemMenu.Build(S, nodes).Open(_list);
            e.Handled = true;
        };
        var r1 = FN.Rule();
        var r2 = FN.Rule();
        var r3 = FN.Rule();
        SetDock(_filters, Dock.Top);
        SetDock(r1, Dock.Top);
        SetDock(_header, Dock.Top);
        SetDock(r2, Dock.Top);
        SetDock(_footer, Dock.Bottom);
        SetDock(r3, Dock.Bottom);
        Children.Add(_filters);
        Children.Add(r1);
        Children.Add(_header);
        Children.Add(r2);
        Children.Add(_footer);
        Children.Add(r3);
        Children.Add(_list);
        Refresh(Topic.Filter);
    }

    public void Refresh(Topic t)
    {
        bool changed = !Equals(_lastFilter, S.Filter) || _lastCount != S.Derived.Files.Count || _lastVersion != S.TreeVersion || _lastMode != S.SizeMode;
        if (!changed) return;
        // Rebuild the filter bar only when something other than the typed text changed (keeps focus while typing).
        if (_lastFilter == null || _filters.Content == null || (S.Filter with { Text = "" }) != (_lastFilter with { Text = "" })) _filters.Content = Filters();
        else if (_text != null && !_text.IsFocused && _text.Text != S.Filter.Text) _text.Text = S.Filter.Text;
        _lastFilter = S.Filter;
        _lastCount = S.Derived.Files.Count;
        _lastVersion = S.TreeVersion;
        _lastMode = S.SizeMode;
        _header.Content = Header();
        Recompute();
    }

    private void Recompute()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        _computing = true;
        _footer.Content = Footer();
        var files = S.Derived.Files;
        var f = S.Filter;
        var mode = S.SizeMode;
        string key = _sortKey;
        bool asc = _asc;
        Task.Run(() =>
        {
            string needle = f.Text.Trim();
            long cutoff = f.OlderThanDays > 0 ? DateTime.UtcNow.AddDays(-f.OlderThanDays).Ticks : 0;
            var outp = new List<FileNode>();
            int count = 0;
            long total = 0;
            foreach (var n in files)
            {
                if (cts.IsCancellationRequested) return;
                if (n.Size(mode) < f.MinSize) continue;
                if (cutoff > 0 && n.ModifiedTicks >= cutoff) continue;
                if (f.Ext != null && n.Extension != f.Ext && !(f.Ext == "(none)" && n.Extension.Length == 0)) continue;
                if (f.Category is { } c && FileCategoryExt.Of(n) != c) continue;
                if (needle.Length > 0 && n.Name.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                count++;
                total += n.Size(mode);
                if (outp.Count < f.Limit) outp.Add(n);
            }
            var sorted = Sort(outp, key, asc, mode);
            Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested) return;
                _results = sorted;
                _matchCount = count;
                _matchSize = total;
                _computing = false;
                _list.ItemsSource = _results;
                _footer.Content = Footer();
            });
        });
    }

    private static List<FileNode> Sort(List<FileNode> items, string key, bool asc, SizeMode mode)
    {
        IOrderedEnumerable<FileNode> q = key switch
        {
            "name" => asc ? items.OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase) : items.OrderByDescending(n => n.Name, StringComparer.CurrentCultureIgnoreCase),
            "modified" => asc ? items.OrderBy(n => n.ModifiedTicks) : items.OrderByDescending(n => n.ModifiedTicks),
            "kind" => asc ? items.OrderBy(n => FileCategoryExt.Of(n).Title()) : items.OrderByDescending(n => FileCategoryExt.Of(n).Title()),
            _ => asc ? items.OrderBy(n => n.Size(mode)) : items.OrderByDescending(n => n.Size(mode)),
        };
        return q.ToList();
    }

    private void SortBy(string key)
    {
        if (_sortKey == key) _asc = !_asc; else { _sortKey = key; _asc = key is "name" or "kind"; }
        _header.Content = Header();
        _results = Sort(_results, _sortKey, _asc, S.SizeMode);
        _list.ItemsSource = _results;
    }

    private Control Filters()
    {
        var f = S.Filter;
        var text = _text = FN.TextField("Name contains…", f.Text, t => { if (t != S.Filter.Text) S.SetFilter(S.Filter with { Text = t }); }, 180);
        var kind = FN.MenuPicker("Kind", new (FileCategory?, string)[] { (null, "All kinds") }.Concat(Enum.GetValues<FileCategory>().Select(c => ((FileCategory?)c, c.Title()))),
                                 f.Category, c => S.SetFilter(S.Filter with { Category = c }));
        var min = FN.MenuPicker("Min", new (long, string)[] { (0, "Any size"), (1_000_000, "1 MB"), (10_000_000, "10 MB"), (100_000_000, "100 MB"), (500_000_000, "500 MB"), (1_000_000_000, "1 GB"), (5_000_000_000, "5 GB") },
                                f.MinSize, v => S.SetFilter(S.Filter with { MinSize = v }));
        var old = FN.MenuPicker("Untouched", new (int, string)[] { (0, "Any time"), (30, "30 days"), (90, "90 days"), (180, "6 months"), (365, "1 year"), (730, "2 years"), (1825, "5 years") },
                                f.OlderThanDays, v => S.SetFilter(S.Filter with { OlderThanDays = v }));
        var left = FN.H(FN.S2, text, kind, min, old);
        if (f.Ext != null) left.Children.Add(FN.Button("." + f.Ext, FNButton.Kind.Primary, () => S.SetFilter(S.Filter with { Ext = null }), "x", compact: true));
        var reset = FN.Button("Reset", FNButton.Kind.Secondary, () => S.SetFilter(new FilesFilter()), compact: true);
        reset.IsEnabled = f != new FilesFilter();
        return new Border { Child = FN.Row(left, reset), Padding = new Thickness(FN.S5, FN.S2) };
    }

    private Control Header()
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Margin = new Thickness(FN.S5, 9) };
        void Col(int i, string title, string? key, bool right = false)
        {
            var h = FN.ColumnHeader(title, key != null && _sortKey == key, _asc, right, key == null ? null : () => SortBy(key));
            h.Margin = new Thickness(i == 0 ? 0 : FN.S2, 0, 0, 0);
            Grid.SetColumn(h, i);
            g.Children.Add(h);
        }
        Col(0, "Name", "name");
        Col(1, S.SizeMode.Title(), "size", true);
        Col(2, "Modified", "modified");
        Col(3, "Kind", "kind");
        Col(4, "Safety", null);
        Col(5, "Location", null);
        return g;
    }

    private Control BuildRow(FileNode n)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Columns), Height = 28, Margin = new Thickness(FN.S5, 0) };
        var cat = FileCategoryExt.Of(n);
        var name = FN.IconRow(FN.S1, FN.Icon(FN.IconFor(n), 11, cls: "muted"), FN.T(n.Name, 12, cls: "fg"));
        ToolTip.SetTip(name, n.FullPath);
        Put(g, 0, name);
        var size = FN.T(Fmt.Bytes(n.Size(S.SizeMode)), 12, weight: FontWeight.SemiBold, cls: "fg");
        size.HorizontalAlignment = HorizontalAlignment.Right;
        Put(g, 1, size);
        Put(g, 2, FN.T(Fmt.ShortDate(n.ModifiedTicks), 12, cls: "muted"));
        var sw = FN.Swatch(cat.Brush());
        sw.Classes.Add("swatch");
        Put(g, 3, FN.IconRow(6, sw, FN.T(cat.Title(), 12, cls: "muted")));
        var level = SafetyClassifier.Classify(n).Level;
        Put(g, 4, FN.Pill(level.Short(), level.Pill()));
        Put(g, 5, FN.T(Fmt.Abbreviate(n.Parent?.FullPath ?? ""), 12, cls: "muted"));
        return new Border { Child = g, BorderBrush = FN.Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent };
    }

    private static void Put(Grid g, int col, Control c)
    {
        if (col > 0) c.Margin = new Thickness(FN.S2, 0, 0, 0);
        c.VerticalAlignment = VerticalAlignment.Center;
        c.ClipToBounds = true;
        Grid.SetColumn(c, col);
        g.Children.Add(c);
    }

    private Control Footer()
    {
        var selected = _list.SelectedItems?.OfType<FileNode>().ToList() ?? new();
        long selSize = selected.Sum(n => n.Size(S.SizeMode));
        var info = FN.T($"{Fmt.Count(_matchCount)} files · {Fmt.Bytes(_matchSize)}" + (_matchCount > _results.Count ? $" · largest {Fmt.Count(_results.Count)} shown" : "") + (_computing ? " · filtering…" : ""), 11, FN.Muted);
        var limit = FN.MenuPicker(null, new[] { (500, "Show 500"), (2000, "Show 2,000"), (10000, "Show 10,000") }, S.Filter.Limit, v => S.SetFilter(S.Filter with { Limit = v }));
        var left = FN.H(FN.S2, info, limit);
        var right = FN.H(FN.S2);
        if (selected.Count > 0)
        {
            right.Children.Add(FN.T($"{selected.Count} selected · {Fmt.Bytes(selSize)}", 11, FN.Fg, FontWeight.SemiBold));
            right.Children.Add(FN.Button(Platform.IsWindows ? "Show" : "Reveal", FNButton.Kind.Secondary, () => Platform.Reveal(selected.Select(n => n.FullPath)), compact: true));
            right.Children.Add(FN.Button($"Move to {ItemMenu.RecycleName}…", FNButton.Kind.Danger, () => S.RequestDelete(selected, false), compact: true));
            right.Children.Add(FN.Button("Delete…", FNButton.Kind.Danger, () => S.RequestDelete(selected, true), compact: true));
        }
        return new Border { Child = FN.Row(left, right), Padding = new Thickness(FN.S5, FN.S1) };
    }
}

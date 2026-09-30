using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DiskWatch.Core;

namespace DiskWatch.UI;

public interface IView
{
    void Refresh(Topic topics);
}

public sealed class MainWindow : Window
{
    private readonly AppState S;
    private readonly ContentControl _sidebar = new(), _header = new(), _headerTop = new(), _tabs = new(), _banners = new(), _content = new(), _status = new(), _inspector = new(), _overlay = new();
    private readonly Border _inspectorColumn;
    private readonly Dictionary<Tab, Control> _views = new();
    private FileNode? _viewsRoot;
    private TextBox? _search;
    private ScanOverlay? _scanOverlay;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private Topic _pending;

    public MainWindow(AppState state)
    {
        S = state;
        Title = "DiskWatch";
        Width = 1440;
        Height = 900;
        MinWidth = 1100;
        MinHeight = 680;
        Background = FN.Bg;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 32;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://DiskWatch/Assets/DiskWatch.ico"))); } catch { }

        // Title strip: empty, draggable; Windows caption buttons / macOS traffic lights sit over it.
        var strip = new Border { Height = 32, Background = FN.Bg };
        strip.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        strip.DoubleTapped += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        var main = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        DockPanel.SetDock(_banners, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        var headerRule = FN.Rule();
        DockPanel.SetDock(headerRule, Dock.Top);
        var statusRule = FN.Rule();
        DockPanel.SetDock(statusRule, Dock.Bottom);
        main.Children.Add(_header);
        main.Children.Add(headerRule);
        main.Children.Add(_banners);
        main.Children.Add(_status);
        main.Children.Add(statusRule);
        main.Children.Add(_content);

        _inspectorColumn = new Border { Width = 321, BorderBrush = FN.Line, BorderThickness = new Thickness(1, 0, 0, 0), Child = _inspector };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("256,Auto,*,Auto") };
        var sidebarRule = FN.Rule(vertical: true);
        Grid.SetColumn(sidebarRule, 1);
        Grid.SetColumn(main, 2);
        Grid.SetColumn(_inspectorColumn, 3);
        body.Children.Add(_sidebar);
        body.Children.Add(sidebarRule);
        body.Children.Add(main);
        body.Children.Add(_inspectorColumn);

        var layout = new DockPanel();
        DockPanel.SetDock(strip, Dock.Top);
        layout.Children.Add(strip);
        layout.Children.Add(body);

        var root = new Grid();
        root.Children.Add(layout);
        root.Children.Add(_overlay);
        Content = root;

        S.NeedsElevation = !Platform.IsAdmin;
        S.Changed += OnChanged;
        _debounce.Tick += (_, _) => { _debounce.Stop(); var t = _pending; _pending = Topic.None; Apply(t); };
        KeyDown += OnKey;
        Apply((Topic)~0);
        Opened += (_, _) => Snapshot.Start(this, S);
    }

    private void OnChanged(Topic t)
    {
        // Cleanup/duplicate measurements arrive in bursts; coalesce them.
        if ((t & ~(Topic.Cleanup | Topic.Duplicates)) == 0)
        {
            _pending |= t;
            if (!_debounce.IsEnabled) _debounce.Start();
            return;
        }
        Apply(t);
    }

    private void Apply(Topic t)
    {
        if (S.NeedsElevation)
        {
            _sidebar.Content = null;
            _header.Content = null;
            _headerTop.Content = null;
            _tabs.Content = null;
            _status.Content = null;
            _inspectorColumn.IsVisible = false;
            _content.Content = Elevation();
            return;
        }
        bool hasScan = S.Root != null || S.IsScanning;
        if (t.HasFlag(Topic.Scan) || !ReferenceEquals(_viewsRoot, S.Root))
        {
            _views.Clear();
            _viewsRoot = S.Root;
        }
        if (t.HasFlag(Topic.Scan) || t.HasFlag(Topic.Volumes) || t.HasFlag(Topic.Settings) || t.HasFlag(Topic.Derived)) _sidebar.Content = Sidebar();
        _header.Content ??= new Border { Child = FN.V(FN.S3, _headerTop, _tabs), Padding = new Thickness(FN.S5, 0, FN.S5, FN.S3) };
        if (t.HasFlag(Topic.Scan) || t.HasFlag(Topic.Settings)) _headerTop.Content = HeaderTop();
        if (t.HasFlag(Topic.Scan) || t.HasFlag(Topic.Tab)) _tabs.Content = FN.Segmented(Enum.GetValues<Tab>().Select(x => (x, x.ToString())), S.Tab, S.SetTab, disabled: S.Root == null);
        if (_search != null && t.HasFlag(Topic.Filter) && !_search.IsFocused && _search.Text != S.Filter.Text) _search.Text = S.Filter.Text;
        if (t.HasFlag(Topic.Scan) || t.HasFlag(Topic.Derived)) _banners.Content = Banners();
        if (hasScan && (t & (Topic.Scan | Topic.Tree | Topic.Derived | Topic.Selection | Topic.Settings)) != 0) _status.Content = StatusBar();
        if (!hasScan) _status.Content = null;

        if (!hasScan)
        {
            _content.Content = Welcome();
        }
        else if (S.Root != null)
        {
            if (t.HasFlag(Topic.Tab) || t.HasFlag(Topic.Scan) || _content.Content is null or ScrollViewer { Tag: "welcome" })
                _content.Content = ViewFor(S.Tab);
            foreach (var v in _views.Values.OfType<IView>()) v.Refresh(t);
        }

        _inspectorColumn.IsVisible = S.ShowInspector && S.Root != null;
        if (_inspectorColumn.IsVisible && (t & (Topic.Selection | Topic.Tree | Topic.Scan | Topic.Settings | Topic.Derived)) != 0)
            _inspector.Content = new InspectorView(S);

        if (S.IsScanning)
        {
            if (_scanOverlay == null) _overlay.Content = _scanOverlay = new ScanOverlay(S);
            _scanOverlay.Update();
        }
        else
        {
            _scanOverlay = null;
            _overlay.Content = S.Deletion != null ? Modal(new DeleteDialog(S, S.Deletion), 660) : null;
        }
        if (t.HasFlag(Topic.Error) && S.ErrorMessage != null) ShowError(S.ErrorMessage);
    }

    private Control ViewFor(Tab tab)
    {
        if (_views.TryGetValue(tab, out var v)) return v;
        v = tab switch
        {
            Tab.Overview => new OverviewView(S),
            Tab.Tree => new TreeOutline(S),
            Tab.Treemap => new TreemapView(S),
            Tab.Files => new FilesView(S),
            Tab.Types => new TypesView(S),
            Tab.Cleanup => new CleanupView(S),
            _ => new DuplicatesView(S),
        };
        _views[tab] = v;
        return v;
    }

    public void ShowModal(Control content, double width) => _overlay.Content = Modal(content, width);
    public void CloseModal() => _overlay.Content = null;

    public static Control Modal(Control content, double width)
    {
        var panel = new Border
        {
            Width = width,
            Background = FN.Surface,
            BorderBrush = FN.Hair,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(FN.S6),
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new Grid { Background = FN.Scrim, Children = { panel } };
    }

    private void ShowError(string msg)
    {
        S.ErrorMessage = null;
        var ok = FN.Button("OK", FNButton.Kind.Primary, () => _overlay.Content = null);
        _overlay.Content = Modal(FN.V(FN.S5, FN.SectionHeading("DiskWatch"), FN.Caption(msg, FN.Fg), new Border { Child = ok, HorizontalAlignment = HorizontalAlignment.Right }), 480);
    }

    // MARK: Keyboard

    private void OnKey(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (S.Deletion != null && _overlay.Content != null)
        {
            if (e.Key == Key.Escape) { S.CloseDeletion(); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape && S.IsScanning) { S.CancelScan(); e.Handled = true; return; }
        if (e.Source is TextBox && e.Key is Key.Delete or Key.Back) return;
        switch (e.Key)
        {
            case Key.O when ctrl: _ = ChooseFolder(); break;
            case Key.R when ctrl: case Key.F5: S.Rescan(); break;
            case Key.E when ctrl: _ = Export(); break;
            case Key.F when ctrl: _search?.Focus(); break;
            case Key.I when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Alt): S.ToggleInspector(); break;
            case Key.S when ctrl && shift: S.SetSizeMode(S.SizeMode.Other()); break;
            case >= Key.D1 and <= Key.D7 when ctrl && S.Root != null: S.SetTab((Tab)(e.Key - Key.D1)); break;
            case Key.Delete when S.Selection.Count > 0: S.RequestDelete(S.Selection, permanent: shift); break;
            default: return;
        }
        e.Handled = true;
    }

    public async Task ChooseFolder()
    {
        var res = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a folder to scan", AllowMultiple = false });
        if (res.Count > 0 && res[0].TryGetLocalPath() is string p) S.Scan(p);
    }

    public async Task Export()
    {
        if (S.Root == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export report", SuggestedFileName = "DiskWatch Report.csv", DefaultExtension = "csv" });
        if (file?.TryGetLocalPath() is string p) S.ExportCsv(p);
    }

    // MARK: Sidebar

    private Control Sidebar()
    {
        var stack = new StackPanel { Spacing = FN.S6 };
        stack.Children.Add(new Border { Child = FN.Wordmark(), Padding = new Thickness(FN.S4, FN.S2, FN.S4, 0) });

        var vols = Group("Volumes");
        foreach (var v in S.Volumes) vols.Children.Add(VolumeRow(v, S.ScanPath == v.Path));
        stack.Children.Add(vols);

        var places = Group("Places");
        foreach (var (name, path, icon) in Places())
            places.Children.Add(SideRow(name, icon, string.Equals(S.ScanPath, path, StringComparison.OrdinalIgnoreCase), () => S.Scan(path), path));
        stack.Children.Add(places);

        if (S.Settings.RecentPaths.Count > 0)
        {
            var recent = Group("Recent");
            foreach (var p in S.Settings.RecentPaths) recent.Children.Add(SideRow(Fmt.Abbreviate(p), "clock", false, () => S.Scan(p), p));
            stack.Children.Add(recent);
        }

        var status = Group("Status");
        var rows = FN.V(FN.S2,
            FN.Row(FN.Data("Privileges", FN.Muted), FN.Pill(Platform.IsAdmin ? "Admin" : "Limited", Platform.IsAdmin ? FN.PillState.Ok : FN.PillState.Warn)),
            FN.Row(FN.Data("Unreadable", FN.Muted), S.Root == null ? FN.Pill("—", FN.PillState.Neutral)
                : S.Derived.Unreadable.Count == 0 ? FN.Pill("None", FN.PillState.Ok) : FN.Pill($"{S.Derived.Unreadable.Count} folders", FN.PillState.Warn)));
        rows.Margin = new Thickness(FN.S4, 0);
        ToolTip.SetTip(rows, Platform.IsAdmin ? $"Running as administrator on behalf of {Platform.UserName}." : "Running without administrator rights.");
        status.Children.Add(rows);
        stack.Children.Add(status);

        var scanBtn = FN.Button("Scan Folder…", FNButton.Kind.Primary, () => _ = ChooseFolder(), "folderplus");
        var bottom = new Border { Child = scanBtn, Padding = new Thickness(FN.S4), BorderBrush = FN.Line, BorderThickness = new Thickness(0, 1, 0, 0) };
        var dock = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);
        dock.Children.Add(new ScrollViewer { Content = new Border { Child = stack, Padding = new Thickness(0, 0, 0, FN.S5) } });
        return dock;
    }

    private static IEnumerable<(string, string, string)> Places()
    {
        string h = Platform.Home;
        var list = Platform.IsWindows
            ? new List<(string, string, string)>
            {
                ("Home", h, "home"), ("Program Files", Platform.ProgramFiles, "grid"), ("Program Files (x86)", Platform.ProgramFilesX86, "grid"),
                ("Downloads", Platform.Downloads, "download"), ("Documents", Path.Join(h, "Documents"), "doc"), ("Desktop", Path.Join(h, "Desktop"), "monitor"),
                ("AppData (Local)", Platform.LocalAppData, "books"), ("AppData (Roaming)", Platform.RoamingAppData, "books"),
                ("ProgramData", Platform.ProgramData, "box"), ("Windows", Platform.WindowsDir, "gear"), ("Users", Platform.UsersDir, "users"),
            }
            : new List<(string, string, string)>
            {
                ("Home", h, "home"), ("Applications", "/Applications", "grid"), ("Downloads", Platform.Downloads, "download"),
                ("Documents", Path.Join(h, "Documents"), "doc"), ("Desktop", Path.Join(h, "Desktop"), "monitor"),
                ("User Library", Path.Join(h, "Library"), "books"), ("System Library", "/Library", "books"), ("Users", "/Users", "users"),
            };
        return list.Where(p => Directory.Exists(p.Item2));
    }

    private static StackPanel Group(string title)
    {
        var s = new StackPanel { Spacing = 2 };
        s.Children.Add(new Border { Child = FN.Label(title), Padding = new Thickness(FN.S4, 0, FN.S4, 4) });
        return s;
    }

    private static Control SideRow(string title, string icon, bool active, Action onClick, string? tip = null)
    {
        var ic = FN.Icon(icon, 12, active ? FN.Bg : FN.Muted);
        var tb = FN.T(title, 12, active ? FN.Bg : FN.Fg);
        var row = new Border
        {
            Child = FN.H(FN.S2, new Border { Child = ic, Width = 16 }, tb),
            Padding = new Thickness(FN.S4, 6),
            Background = active ? FN.Accent : Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        if (!active)
        {
            row.PointerEntered += (_, _) => row.Background = FN.Track;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        }
        row.Tapped += (_, _) => onClick();
        if (tip != null) ToolTip.SetTip(row, tip);
        return row;
    }

    private Control VolumeRow(Platform.Volume v, bool active)
    {
        var ink = active ? FN.Bg : FN.Fg;
        var stack = FN.V(6,
            FN.Row(FN.H(FN.S2, new Border { Child = FN.Icon("drive", 12, active ? FN.Bg : FN.Muted), Width = 16 }, FN.T(v.Name, 12, ink, FontWeight.SemiBold)),
                   FN.T($"{Math.Round(v.Fraction * 100)}%", 11, ink)),
            new Gauge { Fraction = v.Fraction, BarHeight = 8 },
            FN.T($"{Fmt.Bytes(v.Available)} free / {Fmt.Bytes(v.Total)}", 10, active ? FN.Bg : FN.Muted));
        var row = new Border { Child = stack, Padding = new Thickness(FN.S4, FN.S1), Background = active ? FN.Accent : Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        if (!active)
        {
            row.PointerEntered += (_, _) => row.Background = FN.Track;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        }
        row.Tapped += (_, _) => S.Scan(v.Path);
        var menu = new ContextMenu();
        var reveal = new MenuItem { Header = Platform.IsWindows ? "Open in Explorer" : "Reveal in Finder" };
        reveal.Click += (_, _) => Platform.Open(v.Path);
        menu.Items.Add(reveal);
        row.ContextMenu = menu;
        return row;
    }

    // MARK: Header

    private Control HeaderTop()
    {
        bool has = S.Root != null;
        var where = FN.V(4, FN.Label(S.IsScanning ? "Scanning" : has ? "Location" : "No scan"),
                         FN.Body(S.ScanPath != null ? Fmt.Abbreviate(S.ScanPath) : "Choose a volume or folder"));
        _search = FN.TextField("Search files", S.Filter.Text, t =>
        {
            if (t == S.Filter.Text) return;
            S.SetFilter(S.Filter with { Text = t });
            if (t.Length > 0 && S.Tab != Tab.Files && S.Root != null) S.SetTab(Tab.Files);
        }, 200);
        _search.IsEnabled = has;
        var sizePicker = FN.MenuPicker(null, new[] { (SizeMode.Allocated, SizeMode.Allocated.Title()), (SizeMode.Logical, SizeMode.Logical.Title()) }, S.SizeMode, S.SetSizeMode);
        ToolTip.SetTip(sizePicker, "Size on Disk counts allocated clusters (what you actually get back). File Size is the logical length.");
        Control scanBtn = S.IsScanning
            ? FN.Button("Stop", FNButton.Kind.Danger, S.CancelScan, "stop")
            : FN.Button("Rescan", FNButton.Kind.Secondary, S.Rescan, "refresh", tip: "Rescan (F5)");
        scanBtn.IsEnabled = S.IsScanning || S.ScanPath != null;
        var export = FN.IconButton("export", FNButton.Kind.Secondary, () => _ = Export(), "Export a CSV report (Ctrl+E)");
        export.IsEnabled = has;
        var inspector = FN.IconButton("panel", S.ShowInspector ? FNButton.Kind.Primary : FNButton.Kind.Secondary, S.ToggleInspector, "Show or hide the inspector (Ctrl+Alt+I)");

        return FN.Row(where, _search, sizePicker, scanBtn, export, inspector);
    }

    private Control Banners()
    {
        var s = new StackPanel();
        if (S.Root != null && S.Derived.Unreadable.Count > 0 && !Platform.IsAdmin)
            s.Children.Add(Banner(FN.Warn, "lock", "Not running as administrator — some folders can't be measured or cleaned.",
                FN.Button("Relaunch as Admin", FNButton.Kind.Secondary, () => { if (Platform.RelaunchAsAdmin()) Close(); }, compact: true)));
        if (S.ScanWasCancelled)
            s.Children.Add(Banner(FN.Warn, "warn", "Scan stopped early — totals only include what was read before stopping.",
                FN.Button("Rescan", FNButton.Kind.Secondary, S.Rescan, compact: true)));
        return s;
    }

    public static Control Banner(IBrush color, string icon, string text, params Control[] trailing)
    {
        var row = FN.Row(FN.IconRow(FN.S2, FN.Icon(icon, 12, color), FN.Caption(text, FN.Fg)), trailing);
        return new Border { Child = row, Padding = new Thickness(FN.S5, 10), BorderBrush = color, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private Control StatusBar()
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = FN.S4, VerticalAlignment = VerticalAlignment.Center };
        if (S.Root is { } r)
        {
            left.Children.Add(FN.T(Fmt.Bytes(r.Size(S.SizeMode)), 11, FN.Fg));
            left.Children.Add(FN.T($"{Fmt.Count(r.FileCount)} files", 11, FN.Muted));
            left.Children.Add(FN.T($"{Fmt.Count(r.DirCount)} folders", 11, FN.Muted));
            if (S.Derived.Unreadable.Count > 0) left.Children.Add(FN.T($"{S.Derived.Unreadable.Count} unreadable", 11, FN.Warn));
            if (S.IsRebuilding) left.Children.Add(FN.T("updating…", 11, FN.Muted));
        }
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = FN.S4, VerticalAlignment = VerticalAlignment.Center };
        if (S.Selection.Count > 0)
        {
            long total = S.Selection.Where(n => !S.Selection.Any(n.IsDescendantOf)).Sum(n => n.Size(S.SizeMode));
            right.Children.Add(FN.T($"{S.Selection.Count} selected · {Fmt.Bytes(total)}", 11, FN.Fg));
        }
        right.Children.Add(FN.Pill(Platform.IsAdmin ? "Admin" : Platform.UserName, Platform.IsAdmin ? FN.PillState.Ok : FN.PillState.Neutral));
        return new Border { Child = FN.Row(left, right), Padding = new Thickness(FN.S5, 7) };
    }

    // MARK: Welcome & elevation

    private Control Welcome()
    {
        var vols = new StackPanel { Spacing = FN.S5 };
        foreach (var v in S.Volumes) vols.Children.Add(VolumeCapacity(S, v, hero: ReferenceEquals(v, S.Volumes.FirstOrDefault())));
        var levels = new StackPanel { Spacing = FN.S3 };
        foreach (var l in Enum.GetValues<SafetyLevel>())
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*") };
            var text = FN.Caption(l.Explanation());
            text.Margin = new Thickness(FN.S3, 0, 0, 0);
            Grid.SetColumn(text, 1);
            g.Children.Add(FN.Pill(l.Short(), l.Pill()));
            g.Children.Add(text);
            levels.Children.Add(g);
        }
        var page = FN.V(FN.S7,
            FN.V(FN.S2, FN.Wordmark(), FN.Body("See exactly where your disk space went — and safely take it back.", FN.Muted)),
            FN.Section("Volumes", vols, "Pick a volume to scan everything on it."),
            FN.H(FN.S2, FN.Button("Scan Home Folder", FNButton.Kind.Primary, () => S.Scan(Platform.Home), "home"),
                        FN.Button("Choose Folder…", FNButton.Kind.Secondary, () => _ = ChooseFolder(), "folder")),
            FN.Section("Safety levels", levels, "Every item is classified before anything can be removed."));
        page.MaxWidth = 820;
        page.HorizontalAlignment = HorizontalAlignment.Left;
        page.Margin = new Thickness(FN.S7);
        return new ScrollViewer { Content = page, Tag = "welcome" };
    }

    /// <summary>Volume readout: capacity gauge with a 90% ceiling marker.</summary>
    public static Control VolumeCapacity(AppState s, Platform.Volume v, bool hero)
    {
        double f = v.Fraction;
        var state = f > 0.9 ? FN.PillState.Danger : f > 0.8 ? FN.PillState.Warn : FN.PillState.Ok;
        var pct = $"{Math.Round(f * 100)}%";
        var readout = FN.Row(FN.H(FN.S2, hero ? FN.Hero(pct) : FN.Value(pct), FN.Caption($"{v.Name} used", wrap: false)),
                             FN.Pill(state == FN.PillState.Ok ? "Healthy" : state == FN.PillState.Warn ? "Filling up" : "Nearly full", state));
        var labels = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var mid = FN.T($"{Fmt.Bytes(v.Available)} free of {Fmt.Bytes(v.Total)} · ceiling 90%", 11, FN.Muted);
        mid.HorizontalAlignment = HorizontalAlignment.Center;
        var hundred = FN.T("100%", 11, FN.Muted);
        Grid.SetColumn(mid, 1);
        Grid.SetColumn(hundred, 2);
        labels.Children.Add(FN.T("0%", 11, FN.Muted));
        labels.Children.Add(mid);
        labels.Children.Add(hundred);
        var box = new Border { Child = FN.V(10, readout, new Gauge { Fraction = f }, labels), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        box.Tapped += (_, _) => s.Scan(v.Path);
        return box;
    }

    private Control Elevation()
    {
        var status = FN.Caption("DiskWatch runs as an administrator so it can measure and clean every folder on your PC. Protected system files stay locked no matter what.", FN.Muted);
        var err = FN.Caption("", FN.Danger);
        var auth = FN.Button("Run as Administrator", FNButton.Kind.Primary, () =>
        {
            if (Platform.RelaunchAsAdmin()) Close();
            else err.Text = "Windows didn't grant administrator access (the prompt was cancelled or your account isn't an administrator).";
        });
        var quit = FN.Button("Quit", FNButton.Kind.Secondary, Close);
        var page = FN.V(FN.S5, FN.Wordmark(), FN.Section("Administrator access", FN.V(FN.S5, status, err, FN.H(FN.S2, auth, quit)), "Required every launch."));
        page.MaxWidth = 560;
        page.Margin = new Thickness(FN.S8);
        page.HorizontalAlignment = HorizontalAlignment.Center;
        page.VerticalAlignment = VerticalAlignment.Center;
        return page;
    }
}

/// <summary>Scan progress: stretched hero byte count, stat tiles, and an indeterminate sliding bar.</summary>
public sealed class ScanOverlay : ContentControl
{
    private readonly AppState S;
    private readonly TextBlock _files = FN.T("0", 24), _dirs = FN.T("0", 24), _elapsed = FN.T("0.0 s", 24), _rate = FN.Caption("starting…", wrap: false), _path = FN.Caption("", wrap: false);
    private readonly ContentControl _hero = new(), _errors = new();
    private readonly Border _slider = new() { Background = FN.Accent, Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    public ScanOverlay(AppState s)
    {
        S = s;
        Tile(out var ft, "Files", _files);
        Tile(out var dt, "Folders", _dirs);
        Tile(out var et, "Elapsed", _elapsed);
        var track = new Border { Height = 6, Background = FN.Track, BorderBrush = FN.Hair, BorderThickness = new Thickness(1), ClipToBounds = true, Child = _slider };
        var stop = FN.Button("Stop Scan", FNButton.Kind.Danger, S.CancelScan);
        var body = FN.V(FN.S5,
            FN.SectionHeading("Scanning", Fmt.Abbreviate(S.ScanPath ?? "")),
            _hero,
            FN.TileRow(ft, dt, et),
            FN.Row(_rate, _errors),
            track,
            _path,
            new Border { Child = stop, HorizontalAlignment = HorizontalAlignment.Right });
        Content = MainWindow.Modal(body, 560);
        _timer.Tick += (_, _) =>
        {
            double t = (DateTime.Now - S.ScanStarted).TotalSeconds % 1.6 / 1.6;
            double w = track.Bounds.Width;
            _slider.Width = w * 0.18;
            _slider.Margin = new Thickness(w * 1.18 * t - w * 0.18, 0, 0, 0);
        };
        _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private static void Tile(out Control tile, string label, TextBlock value)
    {
        tile = new Border { Background = FN.Surface, BorderBrush = FN.Line, BorderThickness = new Thickness(1), Padding = new Thickness(FN.S4), Child = FN.V(FN.S1, FN.Label(label), value) };
    }

    public void Update()
    {
        var p = S.Progress;
        double elapsed = (DateTime.Now - S.ScanStarted).TotalSeconds;
        _hero.Content = FN.Hero(Fmt.Bytes(p.Bytes));
        _files.Text = Fmt.Count(p.Files);
        _dirs.Text = Fmt.Count(p.Dirs);
        _elapsed.Text = Fmt.Duration(elapsed);
        _rate.Text = elapsed > 1 ? $"{Fmt.Count((long)(p.Files / Math.Max(elapsed, 0.1)))} files/sec" : "starting…";
        _errors.Content = p.Errors > 0 ? FN.Pill($"{p.Errors} unreadable", FN.PillState.Warn) : null;
        _path.Text = Fmt.Abbreviate(p.Path);
    }
}

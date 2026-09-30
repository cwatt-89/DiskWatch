using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DiskWatch.Core;

namespace DiskWatch.UI;

public sealed class CleanupView : DockPanel, IView
{
    private readonly AppState S;
    private readonly ContentControl _body = new(), _footer = new();
    private readonly HashSet<string> _expanded = new();
    private bool _permanent;

    public CleanupView(AppState s)
    {
        S = s;
        SetDock(_footer, Dock.Bottom);
        Children.Add(_footer);
        Children.Add(_body);
        Refresh(Topic.Cleanup);
    }

    public void Refresh(Topic t)
    {
        if ((t & (Topic.Cleanup | Topic.Scan | Topic.Tab)) == 0 && _body.Content != null) return;
        _body.Content = FN.Scroll(new Border { Child = Build(), Padding = new Thickness(FN.S5) });
        _footer.Content = S.Cleanup.HasScanned ? FN.V(0, FN.Rule(), Footer()) : null;
    }

    private void Rebuild() => Refresh(Topic.Cleanup);

    private Control Build()
    {
        var m = S.Cleanup;
        var stack = new StackPanel { Spacing = FN.S5 };
        string caption = m.HasScanned
            ? (m.IsMeasuring ? $"Measuring {m.Measuring.Count} locations…" : $"{m.VisibleItems.Count} locations with reclaimable space")
            : "Checks well-known temp, cache, log, developer and installer locations. Nothing is removed until you confirm.";
        Control headerBody = m.HasScanned
            ? FN.TileRow(FN.StatTile("Reclaimable", Fmt.Bytes(m.TotalFound), valueColor: FN.Accent), FN.StatTile("Selected", Fmt.Bytes(m.SelectedSize)),
                         FN.StatTile("Safe items", Fmt.Count(m.VisibleItems.Count(i => i.Safety == SafetyLevel.Safe))), FN.StatTile("Restore points", Fmt.Count(m.Snapshots.Count)))
            : new Border { Child = FN.Button("Analyze", FNButton.Kind.Primary, m.ScanAll, "spark"), HorizontalAlignment = HorizontalAlignment.Left };
        var reanalyze = m.HasScanned ? FN.Button("Re-analyze", FNButton.Kind.Secondary, m.ScanAll, "refresh") : null;
        if (reanalyze != null) reanalyze.IsEnabled = !m.IsMeasuring;
        stack.Children.Add(FN.Section("Cleanup", headerBody, caption, reanalyze));
        if (!m.HasScanned) return stack;

        foreach (var section in Enum.GetValues<CleanupModel.SectionKind>())
        {
            var items = m.VisibleItems.Where(i => i.Section == section).ToList();
            if (items.Count == 0) continue;
            long sum = items.Sum(i => m.Get(i.Id)?.Size ?? 0);
            var inner = new StackPanel();
            inner.Children.Add(new Border
            {
                Child = FN.Row(FN.Label(CleanupModel.SectionTitle(section), FN.Fg), FN.T(Fmt.Bytes(sum), 11, FN.Muted)),
                Padding = new Thickness(FN.S4, 10),
                BorderBrush = FN.Line,
                BorderThickness = new Thickness(0, 0, 0, 1),
            });
            foreach (var item in items) inner.Children.Add(Row(item));
            stack.Children.Add(new Border { Background = FN.Surface, BorderBrush = FN.Line, BorderThickness = new Thickness(1), Child = inner });
        }

        if (m.Snapshots.Count > 0)
        {
            var list = new StackPanel();
            foreach (var shadow in m.Snapshots)
            {
                var del = FN.Button("Delete…", FNButton.Kind.Danger, () => ConfirmShadow(shadow), compact: true);
                list.Children.Add(new Border
                {
                    Child = FN.Row(FN.V(2, FN.Data(shadow.Created), FN.T($"{shadow.Volume} · {shadow.Id}", 10, FN.Muted)), del),
                    Padding = new Thickness(0, 6),
                    BorderBrush = FN.Line,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                });
            }
            if (m.SnapshotMessage != null) list.Children.Add(FN.Caption(m.SnapshotMessage));
            stack.Children.Add(FN.Section("Restore points", list, "Volume shadow copies used by System Restore and File History. Their space isn't visible in a file scan."));
        }
        return stack;
    }

    private void ConfirmShadow(Platform.Shadow shadow)
    {
        if (ItemMenu.Top is not MainWindow win) return;
        var cancel = FN.Button("Cancel", FNButton.Kind.Secondary, win.CloseModal);
        var delete = FN.Button("Delete Restore Point", FNButton.Kind.Danger, () => { win.CloseModal(); S.Cleanup.DeleteSnapshot(shadow); });
        win.ShowModal(FN.V(FN.S5,
            FN.SectionHeading("Delete restore point", shadow.Created),
            FN.Caption("You won't be able to restore your PC or previous file versions to this point. This can't be undone.", FN.Fg),
            new Border { Child = FN.H(FN.S2, cancel, delete), HorizontalAlignment = HorizontalAlignment.Right }), 520);
    }

    private Control Row(CleanupModel.Item item)
    {
        var m = S.Cleanup;
        var meas = m.Get(item.Id);
        bool has = (meas?.Count ?? 0) > 0;
        var check = FN.Checkbox(m.Checked.Contains(item.Id), on => { m.Toggle(item.Id, on); }, enabled: has);
        var title = FN.H(FN.S1, FN.T(item.Title, 13, FN.Fg, FontWeight.SemiBold), FN.Pill(item.Safety.Short(), item.Safety.Pill()));
        if (item.PermanentOnly) title.Children.Add(FN.Pill("Permanent", FN.PillState.Warn));
        var text = FN.V(4, title, FN.T(item.Detail, 11, FN.Muted, wrap: true));
        Control size = m.IsMeasuringItem(item.Id)
            ? FN.T("measuring…", 11, FN.Muted)
            : meas != null ? FN.V(2, Right(FN.T(Fmt.Bytes(meas.Size), 14, meas.Size > 0 ? FN.Fg : FN.Muted, FontWeight.SemiBold)), Right(FN.T($"{Fmt.Count(meas.Count)} items", 10, FN.Muted)))
            : new Border();
        size.MinWidth = 100;
        bool open = _expanded.Contains(item.Id);
        var chevron = FN.IconButton(open ? "down" : "right", FNButton.Kind.Ghost, () =>
        {
            if (!_expanded.Remove(item.Id)) _expanded.Add(item.Id);
            Rebuild();
        }, "Show what's inside");
        chevron.IsEnabled = meas?.Paths.Count > 0;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        void Put(int c, Control ctl) { ctl.Margin = new Thickness(c == 0 ? 0 : FN.S2, 0, 0, 0); ctl.VerticalAlignment = c == 1 ? VerticalAlignment.Top : VerticalAlignment.Center; Grid.SetColumn(ctl, c); grid.Children.Add(ctl); }
        Put(0, check);
        Put(1, text);
        Put(2, size);
        Put(3, chevron);
        var stack = FN.V(FN.S1, grid);
        if (open && meas != null)
        {
            var paths = new StackPanel { Margin = new Thickness(26, 0, 0, 0) };
            foreach (var (path, sz) in meas.Paths.Take(15))
            {
                var reveal = FN.IconButton("open", FNButton.Kind.Ghost, () => Platform.Reveal(new[] { path }), ItemMenu.RevealName);
                paths.Children.Add(new Border
                {
                    Child = FN.Row(FN.T(path == SafetyClassifier.RecycleBinPath ? "Recycle Bin" : Fmt.Abbreviate(path), 11, FN.Fg), FN.T(Fmt.Bytes(sz), 11, FN.Muted), reveal),
                    Padding = new Thickness(0, 5),
                    BorderBrush = FN.Line,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                });
            }
            if (meas.Paths.Count > 15) paths.Children.Add(FN.T($"+ {meas.Paths.Count - 15} more", 11, FN.Muted));
            stack.Children.Add(paths);
        }
        return new Border { Child = stack, Padding = new Thickness(FN.S4, FN.S2), BorderBrush = FN.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private static Control Right(TextBlock t) { t.HorizontalAlignment = HorizontalAlignment.Right; return t; }

    private Control Footer()
    {
        var m = S.Cleanup;
        var ids = m.Checked.ToList();
        bool permanentOnly = m.AnyPermanentOnly(ids);
        var left = FN.H(FN.S2,
            FN.V(2, FN.Label("Selected"), FN.T(Fmt.Bytes(m.SelectedSize), 14, FN.Fg, FontWeight.SemiBold)),
            FN.Button("Select Safe", FNButton.Kind.Secondary, () => m.SetChecked(m.VisibleItems.Where(i => i.Safety == SafetyLevel.Safe && (m.Get(i.Id)?.Count ?? 0) > 0).Select(i => i.Id)), compact: true),
            FN.Button("Clear", FNButton.Kind.Secondary, () => m.SetChecked(Array.Empty<string>()), compact: true));
        Control mode = permanentOnly
            ? FN.T("Includes Recycle Bin — deleted permanently", 11, FN.Warn)
            : FN.Segmented(new[] { (false, ItemMenu.RecycleName), (true, "Permanent") }, _permanent, v => { _permanent = v; _footer.Content = FN.V(0, FN.Rule(), Footer()); });
        var clean = FN.Button("Clean Up…", FNButton.Kind.Danger, () =>
            S.RequestDelete(m.TargetsFor(ids), _permanent || permanentOnly, permanentOnly, "Cleanup"), "trash");
        clean.IsEnabled = ids.Count > 0 && m.SelectedSize > 0;
        return new Border { Child = FN.Row(left, mode, clean), Padding = new Thickness(FN.S5, FN.S2) };
    }
}

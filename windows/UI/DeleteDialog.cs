using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DiskWatch.Core;

namespace DiskWatch.UI;

/// <summary>
/// Confirmation for Recycle Bin / permanent deletion. Protected items are listed but skipped; dangerous
/// items require typing DELETE; permanent deletion requires an explicit acknowledgment.
/// </summary>
public sealed class DeleteDialog : ContentControl
{
    private readonly AppState S;
    private readonly DeletionRequest R;
    private bool _permanent, _acknowledged, _working;
    private string _typed = "";
    private DeletionResult? _result;
    private FNButton? _go;

    public DeleteDialog(AppState s, DeletionRequest r)
    {
        S = s;
        R = r;
        _permanent = r.Permanent;
        Rebuild();
        AttachedToVisualTree += (_, _) => (TopLevel.GetTopLevel(this) as Window)!.KeyDown += OnKey;
        DetachedFromVisualTree += (_, e) => { if (e.Root is Window w) w.KeyDown -= OnKey; };
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _go != null && _go.IsEnabled && e.Source is not TextBox) { _go.Invoke(); e.Handled = true; }
    }

    private bool CanProceed =>
        R.Allowed.Count > 0 && !_working && (!R.NeedsTypedConfirmation || _typed == "DELETE") && (!_permanent || _acknowledged);

    private void Rebuild() => Content = _result != null ? ResultView(_result) : Confirm();

    private Control Confirm()
    {
        var allowed = R.Allowed;
        var blocked = R.Blocked;
        var worst = allowed.Select(i => i.Verdict.Level).DefaultIfEmpty(SafetyLevel.Safe).Max();
        var counts = FN.H(6);
        foreach (var lvl in Enum.GetValues<SafetyLevel>().Reverse())
        {
            int n = R.Items.Count(i => i.Verdict.Level == lvl);
            if (n > 0) counts.Children.Add(FN.Pill($"{n} {lvl.Short()}", lvl.Pill()));
        }
        string noun = $"{allowed.Count} item{(allowed.Count == 1 ? "" : "s")}";
        var stack = new StackPanel { Spacing = FN.S5 };
        stack.Children.Add(FN.SectionHeading(_permanent ? "Delete permanently" : $"Move to {ItemMenu.RecycleName}",
            $"{noun} · {Fmt.Bytes(R.TotalAllowed)} · from {R.Source}", counts));

        var list = new StackPanel();
        foreach (var item in allowed.OrderByDescending(i => i.Verdict.Level)) list.Children.Add(ItemRow(item));
        if (blocked.Count > 0)
        {
            list.Children.Add(new Border { Child = FN.Label("Skipped — protected", FN.Danger), Padding = new Thickness(FN.S2, FN.S3, FN.S2, FN.S1) });
            foreach (var item in blocked) list.Children.Add(ItemRow(item));
        }
        stack.Children.Add(new Border
        {
            Background = FN.Bg,
            BorderBrush = FN.Line,
            BorderThickness = new Thickness(1),
            MaxHeight = 250,
            Child = new ScrollViewer { Content = list },
        });

        if (!R.LockMode)
            stack.Children.Add(FN.Segmented(new[] { (false, $"Move to {ItemMenu.RecycleName} — recoverable"), (true, "Delete permanently") }, _permanent,
                v => { _permanent = v; Rebuild(); }));
        if (worst == SafetyLevel.Danger)
            stack.Children.Add(Notice(FN.Warn, "Includes items marked DANGER. Removing them can break apps, sync services or Windows features, or delete data that exists nowhere else."));
        if (_permanent)
            stack.Children.Add(Notice(FN.Danger, $"Permanently deleted items skip the {ItemMenu.RecycleName} and cannot be recovered by DiskWatch."));
        if (R.NeedsTypedConfirmation)
        {
            var field = FN.TextField("DELETE", _typed, t => { _typed = t; if (_go != null) _go.IsEnabled = CanProceed; }, 160);
            stack.Children.Add(FN.H(FN.S2, FN.Label("Type DELETE to confirm", FN.Fg), field));
        }
        if (_permanent)
            stack.Children.Add(FN.Checkbox(_acknowledged, v => { _acknowledged = v; Rebuild(); }, "I understand this cannot be undone"));

        var cancel = FN.Button("Cancel", FNButton.Kind.Secondary, S.CloseDeletion);
        _go = FN.Button(_working ? "Working…" : _permanent ? "Delete Permanently" : $"Move to {ItemMenu.RecycleName}", FNButton.Kind.Danger, Go);
        _go.IsEnabled = CanProceed;
        var footer = FN.Row(allowed.Count == 0 ? FN.Caption("Nothing here can be removed.") : new Border(), cancel, _go);
        stack.Children.Add(footer);
        return stack;
    }

    private async void Go()
    {
        if (!CanProceed) return;
        _working = true;
        Rebuild();
        R.Permanent = _permanent;
        _result = await S.Perform(R);
        _working = false;
        Rebuild();
    }

    private static Control ItemRow(DeletionItem item)
    {
        var text = FN.V(3, FN.Data(Path.GetFileName(item.Path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } nm ? nm : item.Path),
                           FN.T(item.Path == SafetyClassifier.RecycleBinPath ? "Recycle Bin" : Fmt.Abbreviate(item.Path), 11, FN.Muted));
        if (item.Verdict.Level != SafetyLevel.Safe)
            text.Children.Add(FN.T(string.Join(" ", item.Verdict.Reasons), 11, item.Verdict.Level == SafetyLevel.Caution ? FN.Muted : item.Verdict.Level.Tint(), wrap: true));
        var right = FN.V(4, FN.Data(Fmt.Bytes(item.Size)), FN.Pill(item.Verdict.Level.Short(), item.Verdict.Level.Pill()));
        right.HorizontalAlignment = HorizontalAlignment.Right;
        var g = FN.Row(FN.IconRow(FN.S2, FN.Icon(item.IsDirectory ? "folder" : "doc", 11), text), right);
        return new Border { Child = g, Padding = new Thickness(FN.S2), BorderBrush = FN.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private static Control Notice(IBrush color, string text) => new Border
    {
        Child = FN.IconRow(FN.S2, FN.Icon("warn", 11, color), FN.Caption(text, FN.Fg)),
        Padding = new Thickness(FN.S2),
        BorderBrush = color,
        BorderThickness = new Thickness(1),
    };

    private Control ResultView(DeletionResult r)
    {
        var stack = new StackPanel { Spacing = FN.S5 };
        stack.Children.Add(FN.SectionHeading(r.Failed.Count == 0 ? "Done" : "Finished with problems",
            $"{r.Removed.Count} item{(r.Removed.Count == 1 ? "" : "s")} {(_permanent ? "deleted" : $"moved to the {ItemMenu.RecycleName}")}",
            FN.Pill(r.Failed.Count == 0 ? "Complete" : $"{r.Failed.Count} failed", r.Failed.Count == 0 ? FN.PillState.Ok : FN.PillState.Warn)));
        stack.Children.Add(FN.TileRow(FN.StatTile(_permanent ? "Freed" : $"To {ItemMenu.RecycleName}", Fmt.Bytes(r.Freed), valueColor: FN.Accent),
                                      FN.StatTile("Removed", Fmt.Count(r.Removed.Count)),
                                      FN.StatTile("Failed", Fmt.Count(r.Failed.Count), valueColor: r.Failed.Count == 0 ? FN.Fg : FN.Danger)));
        if (!_permanent && r.Removed.Count > 0) stack.Children.Add(FN.Caption($"Empty the {ItemMenu.RecycleName} to actually free the space."));
        if (r.Failed.Count > 0)
        {
            var list = new StackPanel();
            foreach (var (path, reason) in r.Failed)
                list.Children.Add(new Border
                {
                    Child = FN.V(3, FN.Data(Fmt.Abbreviate(path)), FN.T(reason, 11, FN.Danger, wrap: true)),
                    Padding = new Thickness(FN.S2),
                    BorderBrush = FN.Line,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                });
            stack.Children.Add(new Border { BorderBrush = FN.Line, BorderThickness = new Thickness(1), MaxHeight = 200, Child = new ScrollViewer { Content = list } });
        }
        stack.Children.Add(FN.Caption($"Every action is recorded in {Fmt.Abbreviate(FileOps.LogPath)}."));
        var done = FN.Button("Done", FNButton.Kind.Primary, S.CloseDeletion);
        _go = done;
        stack.Children.Add(FN.Row(FN.H(0, FN.Button("Show Log", FNButton.Kind.Secondary, () => Platform.Reveal(new[] { FileOps.LogPath }))), done));
        return stack;
    }
}

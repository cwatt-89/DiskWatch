using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DiskWatch.Core;

namespace DiskWatch.UI;

/// <summary>
/// Debug aid: DISKWATCH_SNAPSHOT=&lt;dir&gt; scans a folder, visits every tab and writes window PNGs, then quits.
/// Renders the visual tree directly, so it needs no screen-capture permission.
/// </summary>
public static class Snapshot
{
    public static void Start(Window w, AppState s)
    {
        var dir = Environment.GetEnvironmentVariable("DISKWATCH_SNAPSHOT");
        if (dir == null) return;
        var path = Environment.GetEnvironmentVariable("DISKWATCH_SNAPSHOT_PATH") ?? Path.Join(Platform.Home, "Dev");
        if (Environment.GetEnvironmentVariable("DISKWATCH_SNAPSHOT_DELETE") is string sandbox)
        {
            _ = DeleteFlow(w, s, dir, sandbox).ContinueWith(t => { if (t.Exception != null) Console.Error.WriteLine("DELETE TEST FAILED: " + t.Exception); });
            return;
        }
        _ = Run(w, s, dir, path).ContinueWith(t => { if (t.Exception != null) Console.Error.WriteLine("SNAPSHOT FAILED: " + t.Exception); });
    }

    private static async Task Run(Window w, AppState s, string dir, string path)
    {
        Directory.CreateDirectory(dir);
        await Task.Delay(1200);
        Capture(w, dir, "0-welcome");
        s.Scan(path);
        await Task.Delay(300);
        Capture(w, dir, "1-scanning");
        while (s.IsScanning) await Task.Delay(100);
        await Task.Delay(800);
        if (s.Root?.Children.FirstOrDefault() is { } big) s.Select(big);
        foreach (var tab in Enum.GetValues<Tab>())
        {
            s.SetTab(tab);
            if (tab == Tab.Cleanup) { s.Cleanup.ScanAll(); await Task.Delay(300); while (s.Cleanup.IsMeasuring) await Task.Delay(200); }
            if (tab == Tab.Duplicates) { s.Duplicates.MinSize = 100_000; s.Duplicates.Run(s.Derived.Files); await Task.Delay(300); while (s.Duplicates.IsRunning) await Task.Delay(200); }
            await Task.Delay(1500);
            Capture(w, dir, "2-" + tab.ToString().ToLowerInvariant());
        }
        s.SetTab(Tab.Tree);
        var picks = new[] { s.Derived.Files.FirstOrDefault(), s.Root?.Find(Path.Join(Platform.Home, "Library")), s.Root?.Find(Platform.Downloads) }.OfType<FileNode>().ToList();
        s.RequestDelete(picks, permanent: true);
        await Task.Delay(1200);
        Capture(w, dir, "3-delete");
        s.CloseDeletion();
        await Task.Delay(300);
        w.Close();
    }

    /// <summary>
    /// Deletes a throwaway file through the real confirmation dialog and captures each step:
    /// confirm → result → after "Done". <paramref name="sandbox"/> must be a disposable folder.
    /// </summary>
    private static async Task DeleteFlow(Window w, AppState s, string dir, string sandbox)
    {
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(sandbox);
        var victim = Path.Join(sandbox, "delete-me.bin");
        File.WriteAllBytes(victim, new byte[200_000]);
        File.WriteAllBytes(Path.Join(sandbox, "keep-me.bin"), new byte[100_000]);
        await Task.Delay(800);
        s.Scan(sandbox);
        while (s.IsScanning) await Task.Delay(100);
        await Task.Delay(500);
        s.SetTab(Tab.Files);
        var node = s.Root!.Find(victim)!;
        s.RequestDelete(new[] { node }, permanent: true);
        await Task.Delay(600);
        Capture(w, dir, "d1-confirm");
        var dialog = w.GetVisualDescendants().OfType<DeleteDialog>().First();
        dialog.ConfirmForTest();
        await Task.Delay(2500);
        Capture(w, dir, "d2-after-delete");
        var open = w.GetVisualDescendants().OfType<DeleteDialog>().ToList();
        Console.WriteLine($"file deleted: {!File.Exists(victim)}; dialogs open after delete: {open.Count}; same instance: {open.Count == 1 && ReferenceEquals(open[0], dialog)}");
        s.CloseDeletion();
        await Task.Delay(600);
        Capture(w, dir, "d3-after-done");
        Console.WriteLine($"dialogs open after Done: {w.GetVisualDescendants().OfType<DeleteDialog>().Count()}");
        w.Close();
    }

    private static void Capture(Window w, string dir, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var size = new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height);
        using var bmp = new RenderTargetBitmap(size);
        bmp.Render(w);
        bmp.Save(Path.Join(dir, name + ".png"));
    }
}

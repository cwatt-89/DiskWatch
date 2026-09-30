using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DiskWatch.Core;
using DiskWatch.UI;

namespace DiskWatch;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppState.Ui = a => { if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Post(a); };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(new AppState());
        base.OnFrameworkInitializationCompleted();
    }
}

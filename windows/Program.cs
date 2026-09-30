using Avalonia;

namespace DiskWatch;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--selftest"))
        {
            SelfTest.Run(args);
            return;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.Error.WriteLine("UNHANDLED: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => Console.Error.WriteLine("UNOBSERVED: " + e.Exception);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

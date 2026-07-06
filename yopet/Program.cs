using Avalonia;
using System;
using System.IO;

namespace yopet;

class Program
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".petdex", "crash.log");

    [STAThread]
    public static void Main(string[] args)
    {
        // 全局异常捕获
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                File.WriteAllText(CrashLogPath,
                    $"[{DateTime.Now:O}] UnhandledException\n{e.ExceptionObject}");
            }
            catch { }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                File.WriteAllText(CrashLogPath,
                    $"[{DateTime.Now:O}] TaskException\n{e.Exception}");
            }
            catch { }
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(CrashLogPath, $"[{DateTime.Now:O}] Main catch\n{ex}"); }
            catch { }
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

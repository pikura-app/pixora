using Avalonia;
using System;
using System.IO;

namespace Pixora.Avalonia;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixora", "startup_crash.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.WriteAllText(log, e.ExceptionObject?.ToString() ?? "Unknown error");
        };
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixora", "startup_crash.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.WriteAllText(log, ex.ToString());
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

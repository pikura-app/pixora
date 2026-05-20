using Avalonia;
using Pixora.Avalonia.Services;
using System;
using System.IO;

namespace Pixora.Avalonia;

sealed class Program
{
    private static CrashReportService? _crashService;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Initialize crash reporting service early
        _crashService = new CrashReportService();

        // Handle unhandled exceptions before Avalonia starts
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                _crashService?.GenerateCrashReport(ex, "AppDomain - Fatal unhandled exception before/during startup");
            }
            else
            {
                _crashService?.GenerateCrashReport(
                    new Exception("Unknown non-exception error object: " + e.ExceptionObject?.ToString()),
                    "AppDomain - Fatal non-exception error");
            }
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Generate detailed crash report
            _crashService?.GenerateCrashReport(ex, "Startup - Failed to initialize Avalonia application");

            // Also write simple error for immediate visibility
            var simpleLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixora", "startup_error.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(simpleLog)!);
            File.WriteAllText(simpleLog, $"[{DateTime.Now}] Startup failed:\n{ex}\n");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

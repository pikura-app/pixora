using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Pixora.Avalonia.ViewModels;
using Pixora.Avalonia.Views;
using Pixora.Avalonia.Services;
using Pixora.Core.Services;
using Pixora.Core.Settings;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Pixora.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AppServices.Initialize();

        // Apply persisted theme before any window is created
        var settings = AppServices.Get<SettingsService>();
        RequestedThemeVariant = settings.Current.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark"  => ThemeVariant.Dark,
            _       => null  // system default
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Log all unhandled exceptions to Desktop\pixora-crash.txt
        var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "pixora-crash.txt");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            File.AppendAllText(log, $"[{DateTime.Now}] FATAL: {e.ExceptionObject}\n\n");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            File.AppendAllText(log, $"[{DateTime.Now}] TASK: {e.Exception}\n\n");
            e.SetObserved();
        };
        global::Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            File.AppendAllText(log, $"[{DateTime.Now}] UI: {e.Exception}\n\n");
            e.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindowViewModel = AppServices.Get<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
            };

            var dialogService = AppServices.Get<DialogService>();
            dialogService.Initialize(desktop.MainWindow);

            var accessibilityService = AppServices.Get<AccessibilityService>();

            // Auto-validate existing session cookie in background (shared with WPF app)
            var settings = AppServices.Get<SettingsService>();
            if (settings.Current.IsConfigured)
            {
                var client = AppServices.Get<PixivClient>();
                _ = Task.Run(async () =>
                {
                    try { await client.ValidateSessionAsync(); }
                    catch { /* non-fatal — UI will show "not signed in" and let user retry */ }
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayIconClicked(object? sender, EventArgs e) => ShowMainWindow();
    private void OnTrayOpen(object? sender, EventArgs e)       => ShowMainWindow();
    private void OnTrayQuit(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void ShowMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        var w = desktop.MainWindow;
        if (w == null) return;
        w.Show();
        w.WindowState = WindowState.Normal;
        w.Activate();
    }
}
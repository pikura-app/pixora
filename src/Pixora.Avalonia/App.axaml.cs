using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Pixora.Avalonia.ViewModels;
using Pixora.Avalonia.Views;
using Pixora.Avalonia.Services;
using Pixora.Avalonia.Views.Dialogs;
using Pixora.Core.Services;
using Pixora.Core.Settings;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Pixora.Avalonia;

public partial class App : Application
{
    private CrashReportService? _crashService;

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
        // Initialize crash reporting service
        _crashService = new CrashReportService();

        // Set up comprehensive unhandled exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        global::Avalonia.Threading.Dispatcher.UIThread.UnhandledException += OnUIThreadException;

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

            // Eagerly construct HistoryViewModel synchronously so it subscribes to
            // coordinator events before any download is triggered — otherwise jobs
            // started before the user opens the History tab would be missed.
            var historyVm = AppServices.Get<HistoryViewModel>();

            // Pre-load persisted history data after the window finishes initializing.
            _ = global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(200); // let window finish initializing
                try { await historyVm.ReloadAsync(); } catch { }
            });

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

            // Check for previous crash and show dialog after window is loaded
            _ = global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(500); // Let main window fully load first
                await ShowCrashDialogIfNeededAsync();
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Shows the crash report dialog if a crash was detected from previous session.
    /// </summary>
    private async Task ShowCrashDialogIfNeededAsync()
    {
        if (_crashService?.WasCrashDetected() != true) return;

        var crashInfo = _crashService.GetLastCrashInfo();
        if (crashInfo == null) return;

        var dialog = new CrashReportDialog(crashInfo);

        // Get the main window to use as owner
        Window? owner = null;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            owner = desktop.MainWindow;
        }

        await dialog.ShowDialog(owner);
    }

    /// <summary>
    /// Handler for AppDomain unhandled exceptions (fatal - app will terminate).
    /// </summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _crashService?.GenerateCrashReport(ex, $"AppDomain - IsTerminating: {e.IsTerminating}");
        }
        else
        {
            _crashService?.GenerateCrashReport(
                new Exception("Unknown non-exception error: " + e.ExceptionObject?.ToString()),
                "AppDomain - Non-exception error object");
        }
    }

    /// <summary>
    /// Handler for TaskScheduler unobserved task exceptions.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _crashService?.GenerateCrashReport(e.Exception, "TaskScheduler - Unobserved task exception");
        e.SetObserved(); // Mark as observed to prevent process termination
    }

    /// <summary>
    /// Handler for UI thread unhandled exceptions (non-fatal if handled).
    /// </summary>
    private void OnUIThreadException(object? sender, global::Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _crashService?.GenerateCrashReport(e.Exception, "UI Thread - Dispatcher exception");
        e.Handled = true; // Prevent app crash, but log it
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
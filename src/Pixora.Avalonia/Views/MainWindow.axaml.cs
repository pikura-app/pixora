using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Pixora.Avalonia.Services;
using Pixora.Avalonia.ViewModels;
using Pixora.Core.Settings;

namespace Pixora.Avalonia.Views;

public partial class MainWindow : Window
{
    private TrayIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        PropertyChanged += (_, ev) => { if (ev.Property.Name == nameof(WindowState)) UpdateCaptionIcons(); };
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Restore window size
        try
        {
            var settings = AppServices.Get<SettingsService>();
            Width  = settings.Current.WindowWidth  >= 800 ? settings.Current.WindowWidth  : 1200;
            Height = settings.Current.WindowHeight >= 500 ? settings.Current.WindowHeight : 800;
        }
        catch
        {
            Width  = 1200;
            Height = 800;
        }

        // Build tray icon programmatically (Avalonia 12 requires this approach)
        BuildTrayIcon();

        // Initialize services that need the window reference
        try
        {
            var filePicker = AppServices.Get<FilePickerService>();
            filePicker.Initialize(this);

            var dialogService = AppServices.Get<DialogService>();
            dialogService.Initialize(this);
        }
        catch { /* Services may not be available during design time */ }

        // Subscribe to account switches so the chip refreshes
        try
        {
            var accountService = AppServices.Get<AccountService>();
            accountService.ActiveProfileChanged += (_, _) =>
            {
                var vm = DataContext as ViewModels.MainWindowViewModel;
                Dispatcher.UIThread.Post(() =>
                    vm?.GetType()
                        .GetMethod("RefreshUserChip", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?.Invoke(vm, null));
            };
        }
        catch { }

        // Subscribe to changelog notification from ViewModel
        if (DataContext is ViewModels.MainWindowViewModel mainVm)
        {
            mainVm.PropertyChanged += async (_, ev) =>
            {
                if (ev.PropertyName == nameof(ViewModels.MainWindowViewModel.ChangelogAvailable)
                    && mainVm.ChangelogAvailable)
                {
                    await ShowChangelogDialogAsync(mainVm);
                }
            };
        }

        // On macOS, move the hamburger to the right edge of the sidebar column
        // (traffic lights occupy the left ~75px, so Column=0 right-aligned keeps it safe)
        if (OperatingSystem.IsMacOS())
        {
            HamburgerBtn.SetValue(Grid.ColumnProperty, 0);
            HamburgerBtn.HorizontalAlignment = HorizontalAlignment.Right;
            HamburgerBtn.Margin = new Thickness(0, 4, 6, 0);
        }

        LoadGalleryView();
    }

    private async Task ShowChangelogDialogAsync(ViewModels.MainWindowViewModel mainVm)
    {
        try
        {
            var dialog = new Dialogs.ChangelogDialog(
                mainVm.ChangelogVersion,
                mainVm.ChangelogNotes,
                mainVm.ChangelogReleaseUrl);
            await dialog.ShowDialog(this);
            mainVm.DismissChangelogCommand.Execute(null);
        }
        catch { /* non-fatal */ }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            var settings = AppServices.Get<SettingsService>();
            if (WindowState == WindowState.Normal)
                settings.Update(s => { s.WindowWidth = Width; s.WindowHeight = Height; });

            var s = settings.Current;
            if (s.CloseToTray || s.KeepSchedulesRunningInBackground)
            {
                e.Cancel = true;
                Hide();
                return;
            }
        }
        catch { }
    }

    // Cached view instances — reused across navigation so attached controls never miss events
    private Pixora.Avalonia.Views.Gallery.GalleryView? _galleryView;
    private Pixora.Avalonia.Views.Rankings.EnhancedRankingsView? _rankingsView;
    private Pixora.Avalonia.Views.Discover.DiscoverView? _discoverView;
    private Pixora.Avalonia.Views.Settings.SettingsView? _settingsView;
    private Pixora.Avalonia.Views.Bookmarks.BookmarksView? _bookmarksView;
    private Pixora.Avalonia.Views.Hoshi.HoshiView? _hoshiView;
    private Pixora.Avalonia.Views.Analytics.AnalyticsView? _analyticsView;
    private Pixora.Avalonia.Views.History.HistoryView? _historyView;

    private void SetSectionTitle(string section) => Title = $"Pixora — {section}";

    public void LoadGalleryView()
    {
        try
        {
            var vm = AppServices.Get<Pixora.Avalonia.ViewModels.GalleryViewModel>();
            _galleryView ??= new Pixora.Avalonia.Views.Gallery.GalleryView { DataContext = vm };
            MainContentControl.Content = _galleryView;
            SetSectionTitle("Gallery");
        }
        catch (Exception ex)
        {
            var msg = ex.ToString();
            var logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Pixora", "gallery_crash.txt");
            System.IO.File.WriteAllText(logPath, msg);
            MainContentControl.Content = new TextBlock { Text = "Gallery — sign in first", FontSize = 18, Foreground = Brush.Parse("#9CA3AF") };
        }
    }

    private void GalleryButton_Click(object? sender, RoutedEventArgs e) => LoadGalleryView();

    private void HomeButton_Click(object? sender, RoutedEventArgs e) => LoadGalleryView();

    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<Pixora.Avalonia.ViewModels.SettingsViewModel>();
            _settingsView ??= new Pixora.Avalonia.Views.Settings.SettingsView { DataContext = vm };
            MainContentControl.Content = _settingsView;
            SetSectionTitle("Settings");
        }
        catch
        {
            MainContentControl.Content = new TextBlock { Text = "Settings", FontSize = 18, Foreground = Brush.Parse("#9CA3AF") };
            SetSectionTitle("Settings");
        }
    }

    private void RankingsButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<Pixora.Avalonia.ViewModels.EnhancedRankingsViewModel>();
            _rankingsView ??= new Pixora.Avalonia.Views.Rankings.EnhancedRankingsView { DataContext = vm };
            MainContentControl.Content = _rankingsView;
            SetSectionTitle("Rankings");
        }
        catch
        {
            MainContentControl.Content = new TextBlock { Text = "Rankings — sign in first", FontSize = 18, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
    }

    private void DiscoverButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<Pixora.Avalonia.ViewModels.DiscoverViewModel>();
            _discoverView ??= new Pixora.Avalonia.Views.Discover.DiscoverView { DataContext = vm };
            MainContentControl.Content = _discoverView;
            SetSectionTitle("Discover");
            vm.OnNavigatedTo();
        }
        catch (Exception ex)
        {
            MainContentControl.Content = new TextBlock { Text = $"Discover — error: {ex.Message}", FontSize = 18, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
    }

    private void BookmarksButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<Pixora.Avalonia.ViewModels.BookmarksViewModel>();
            _bookmarksView ??= new Pixora.Avalonia.Views.Bookmarks.BookmarksView { DataContext = vm };
            MainContentControl.Content = _bookmarksView;
            SetSectionTitle("Bookmarks");
            vm.OnNavigatedTo();
        }
        catch (Exception ex)
        {
            MainContentControl.Content = new TextBlock { Text = $"Bookmarks — error: {ex.Message}", FontSize = 18, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
    }

    private async void HistoryButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<HistoryViewModel>();
            _historyView ??= new History.HistoryView { DataContext = vm };
            MainContentControl.Content = _historyView;
            SetSectionTitle("History");
            await vm.ReloadAsync();
        }
        catch (Exception ex)
        {
            MainContentControl.Content = new TextBlock { Text = $"History — error: {ex.Message}", FontSize = 18, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
    }

    private void AnalyticsButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<AnalyticsViewModel>();
            _analyticsView ??= new Pixora.Avalonia.Views.Analytics.AnalyticsView { DataContext = vm };
            MainContentControl.Content = _analyticsView;
            SetSectionTitle("Analytics");
        }
        catch (Exception ex)
        {
            MainContentControl.Content = new TextBlock { Text = $"Analytics — error: {ex.Message}", FontSize = 18, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            SetSectionTitle("Analytics");
        }
    }

    private void BatchDownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<BatchDownloadViewModel>();
            MainContentControl.Content = new BatchDownloadView { DataContext = vm };
            SetSectionTitle("Batch Download");
        }
        catch (Exception ex)
        {
            MainContentControl.Content = new TextBlock { Text = $"Batch Download — error: {ex.Message}", FontSize = 18, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
    }

    private void ArtistsButton_Click(object? sender, RoutedEventArgs e)
    {
        MainContentControl.Content = new TextBlock { Text = "Artists — coming soon", FontSize = 18, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        SetSectionTitle("Artists");
    }

    internal void HoshiButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<Pixora.Avalonia.ViewModels.AiViewModel>();
            _hoshiView ??= new Pixora.Avalonia.Views.Hoshi.HoshiView { DataContext = vm };
            MainContentControl.Content = _hoshiView;
            SetSectionTitle("Hoshi");
        }
        catch (Exception ex)
        {
            MainContentControl.Content = new TextBlock { Text = $"Hoshi — {ex.Message}", FontSize = 14, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Margin = new global::Avalonia.Thickness(20) };
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void ResizeBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var pos  = e.GetCurrentPoint(this).Position;
        var w    = Bounds.Width;
        var h    = Bounds.Height;
        const int edge = 8;
        bool left   = pos.X <= edge;
        bool right  = pos.X >= w - edge;
        bool top    = pos.Y <= edge;
        bool bottom = pos.Y >= h - edge;

        var dir = (left, right, top, bottom) switch
        {
            (true,  false, true,  false) => WindowEdge.NorthWest,
            (false, true,  true,  false) => WindowEdge.NorthEast,
            (true,  false, false, true ) => WindowEdge.SouthWest,
            (false, true,  false, true ) => WindowEdge.SouthEast,
            (true,  false, false, false) => WindowEdge.West,
            (false, true,  false, false) => WindowEdge.East,
            (false, false, true,  false) => WindowEdge.North,
            (false, false, false, true ) => WindowEdge.South,
            _ => (WindowEdge?)null
        };

        if (dir.HasValue)
        {
            e.Handled = true;
            BeginResizeDrag(dir.Value, e);
        }
    }

    private void MinimizeBtn_Click(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeBtn_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void FullscreenBtn_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }

    private void CloseBtn_Click(object? sender, RoutedEventArgs e)
        => Close();

    private void UpdateCaptionIcons()
    {
        // Cross-platform Unicode glyphs (Segoe MDL2 isn't available on Linux/macOS).
        // Maximize: U+25A1 (white square) = maximize, U+29C9 (two joined squares) = restore
        if (MaximizeBtn is { } max)
            max.Content = WindowState == WindowState.Maximized ? "\u29C9" : "\u25A1";
        // Fullscreen: U+26F6 (square four corners) = enter, U+2922 (NE-SW arrow) = exit
        if (FullscreenBtn is { } fs)
            fs.Content = WindowState == WindowState.FullScreen ? "\u2922" : "\u26F6";
    }

    private void AccountChip_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var accountService = AppServices.Get<AccountService>();
            var profiles = accountService.Profiles;

            AccountList.Items.Clear();
            foreach (var profile in profiles)
            {
                var p = profile; // capture
                var btn = new Button
                {
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new Border
                            {
                                Background = Brushes.SteelBlue, CornerRadius = new CornerRadius(10),
                                Width = 20, Height = 20,
                                Child = new TextBlock
                                {
                                    Text = (p.DisplayLabel.Length > 0 ? p.DisplayLabel[0].ToString().ToUpper() : "?"),
                                    FontSize = 10, FontWeight = FontWeight.Bold,
                                    Foreground = Brushes.White,
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    VerticalAlignment = VerticalAlignment.Center,
                                }
                            },
                            new StackPanel
                            {
                                Spacing = 1,
                                Children =
                                {
                                    new TextBlock { Text = p.DisplayLabel, FontSize = 12, FontWeight = FontWeight.SemiBold },
                                    new TextBlock { Text = p.UserId != null ? $"ID: {p.UserId}" : "Not verified", FontSize = 10,
                                                    Foreground = new SolidColorBrush(Color.Parse("#888888")) }
                                }
                            }
                        }
                    },
                    Background = accountService.ActiveProfile?.Id == p.Id
                        ? new SolidColorBrush(Color.Parse("#1A5599FF"))
                        : Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(6),
                    CornerRadius = new CornerRadius(4),
                };
                btn.Click += async (_, _) =>
                {
                    AppServices.Get<AccountService>().SwitchTo(p.Id);
                    AccountChipBtn.Flyout?.Hide();
                    RefreshUserChipFromView();
                    await RefreshGalleryAsync();
                };
                AccountList.Items.Add(btn);
            }
        }
        catch { /* non-fatal */ }
    }

    private async void AddAccountBtn_Click(object? sender, RoutedEventArgs e)
    {
        AccountChipBtn.Flyout?.Hide();
        try
        {
            var loginWindow = new Login.PixivLoginWindow(clearCookiesForNewAccount: true);
            await loginWindow.ShowDialog(this);
            if (loginWindow.LoginSucceeded)
            {
                RefreshUserChipFromView();
                await RefreshGalleryAsync();
            }
        }
        catch { /* non-fatal */ }
    }

    private void RefreshUserChipFromView()
    {
        var vm = DataContext as ViewModels.MainWindowViewModel;
        vm?.GetType().GetMethod("RefreshUserChip",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(vm, null);
    }

    private async Task RefreshGalleryAsync()
    {
        try
        {
            var galleryVm = AppServices.Get<ViewModels.GalleryViewModel>();
            await galleryVm.SwitchAccountAsync();
        }
        catch { /* non-fatal */ }
    }

    // ── Tray icon (programmatic) ──────────────────────────────────────────────
    private void BuildTrayIcon()
    {
        try
        {
            var openItem = new NativeMenuItem("Open Pixora");
            openItem.Click += (_, _) => ShowFromTray();

            var pauseItem = new NativeMenuItem("Pause schedules");
            pauseItem.Click += (_, _) =>
            {
                try { AppServices.Get<Pixora.Core.Services.ScheduleExecutorService>().Stop(); }
                catch { }
            };

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (_, _) =>
            {
                Closing -= OnClosing;
                Close();
            };

            var menu = new NativeMenu();
            menu.Add(openItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(pauseItem);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(exitItem);

            _trayIcon = new TrayIcon
            {
                ToolTipText = "Pixora",
                Icon        = new WindowIcon(global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://Pixora/Assets/pixora-logo.png"))),
                Menu        = menu,
                IsVisible   = true,
            };
            _trayIcon.Clicked += (_, _) => ShowFromTray();

            // Register so Avalonia lifecycle knows about it
            TrayIcon.SetIcons(global::Avalonia.Application.Current!, new TrayIcons { _trayIcon });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TrayIcon init failed: {ex.Message}");
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HamburgerBtn_Click(object? sender, RoutedEventArgs e)
    {
        var col = RootGrid.ColumnDefinitions[0];
        if (col.Width.Value > 0)
        {
            col.Width = new GridLength(0);
            SidebarBorder.IsVisible = false;
        }
        else
        {
            col.Width = new GridLength(220);
            SidebarBorder.IsVisible = true;
        }
    }
}
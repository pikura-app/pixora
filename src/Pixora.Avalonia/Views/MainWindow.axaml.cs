using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Restore window size
        try
        {
            var settings = AppServices.Get<SettingsService>();
            if (settings.Current.WindowWidth >= 800)
                Width = settings.Current.WindowWidth;
            if (settings.Current.WindowHeight >= 500)
                Height = settings.Current.WindowHeight;
        }
        catch { }

        // Initialize services that need the window reference
        try
        {
            var filePicker = AppServices.Get<FilePickerService>();
            filePicker.Initialize(this);

            var dialogService = AppServices.Get<DialogService>();
            dialogService.Initialize(this);
        }
        catch { /* Services may not be available during design time */ }

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

        if (OperatingSystem.IsMacOS())
            HamburgerBtn.Margin = new Thickness(78, 4, 0, 0);

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
            if (WindowState == WindowState.Normal)
            {
                var settings = AppServices.Get<SettingsService>();
                settings.Update(s =>
                {
                    s.WindowWidth = Width;
                    s.WindowHeight = Height;
                });
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

    public void LoadGalleryView()
    {
        try
        {
            var vm = AppServices.Get<Pixora.Avalonia.ViewModels.GalleryViewModel>();
            _galleryView ??= new Pixora.Avalonia.Views.Gallery.GalleryView { DataContext = vm };
            MainContentControl.Content = _galleryView;
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
        }
        catch
        {
            MainContentControl.Content = new TextBlock { Text = "Settings", FontSize = 18, Foreground = Brush.Parse("#9CA3AF") };
        }
    }

    private void RankingsButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<Pixora.Avalonia.ViewModels.EnhancedRankingsViewModel>();
            _rankingsView ??= new Pixora.Avalonia.Views.Rankings.EnhancedRankingsView { DataContext = vm };
            MainContentControl.Content = _rankingsView;
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
            vm.OnNavigatedTo();
        }
        catch (Exception ex)
        {
            MainContentControl.Content = new TextBlock { Text = $"Bookmarks — error: {ex.Message}", FontSize = 18, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
    }

    private void HistoryButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<HistoryViewModel>();
            MainContentControl.Content = new History.HistoryView { DataContext = vm };
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
        }
        catch (Exception ex)
        {
            MainContentControl.Content = new TextBlock { Text = $"Analytics — error: {ex.Message}", FontSize = 18, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
    }

    private void BatchDownloadButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<BatchDownloadViewModel>();
            MainContentControl.Content = new BatchDownloadView { DataContext = vm };
        }
        catch (Exception ex)
        {
            MainContentControl.Content = new TextBlock { Text = $"Batch Download — error: {ex.Message}", FontSize = 18, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
    }

    private void ArtistsButton_Click(object? sender, RoutedEventArgs e)
    {
        MainContentControl.Content = new TextBlock { Text = "Artists — coming soon", FontSize = 18, Foreground = Brush.Parse("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    }

    internal void HoshiButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var vm = AppServices.Get<Pixora.Avalonia.ViewModels.AiViewModel>();
            _hoshiView ??= new Pixora.Avalonia.Views.Hoshi.HoshiView { DataContext = vm };
            MainContentControl.Content = _hoshiView;
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
                btn.Click += (_, _) =>
                {
                    AppServices.Get<AccountService>().SwitchTo(p.Id);
                    AccountChipBtn.Flyout?.Hide();
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
            var loginWindow = new Login.PixivLoginWindow();
            await loginWindow.ShowDialog(this);
            if (loginWindow.LoginSucceeded)
            {
                var vm = DataContext as ViewModels.MainWindowViewModel;
                vm?.GetType().GetMethod("RefreshUserChip",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(vm, null);
            }
        }
        catch { /* non-fatal */ }
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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pixora.Avalonia.Services;
using Pixora.Avalonia.ViewModels;
using Pixora.Avalonia.Views.Artwork;
using Pixora.Avalonia.Views.Dialogs;
using Pixora.Core.Models;
using Pixora.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pixora.Avalonia.Views.Gallery;

public partial class InlineArtworkViewer : UserControl
{
    /// <summary>Bubbling event raised when the Browse button is clicked. Hosts can handle this to toggle their own panel.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> ToggleBrowseEvent =
        RoutedEvent.Register<InlineArtworkViewer, RoutedEventArgs>(nameof(ToggleBrowse), RoutingStrategies.Bubble);
    public event EventHandler<RoutedEventArgs> ToggleBrowse
    {
        add => AddHandler(ToggleBrowseEvent, value);
        remove => RemoveHandler(ToggleBrowseEvent, value);
    }

    /// <summary>Bubbling event raised when the Expand button is clicked inside the viewer. Hosts should go full-screen (hide side panel).</summary>
    public static readonly RoutedEvent<RoutedEventArgs> ExpandViewerEvent =
        RoutedEvent.Register<InlineArtworkViewer, RoutedEventArgs>(nameof(ExpandViewer), RoutingStrategies.Bubble);
    public event EventHandler<RoutedEventArgs> ExpandViewer
    {
        add => AddHandler(ExpandViewerEvent, value);
        remove => RemoveHandler(ExpandViewerEvent, value);
    }

    /// <summary>Bubbling event raised after Close All is executed. Hosts can handle this to close their own panel.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> ViewerClosedEvent =
        RoutedEvent.Register<InlineArtworkViewer, RoutedEventArgs>(nameof(ViewerClosed), RoutingStrategies.Bubble);
    public event EventHandler<RoutedEventArgs> ViewerClosed
    {
        add => AddHandler(ViewerClosedEvent, value);
        remove => RemoveHandler(ViewerClosedEvent, value);
    }

    /// <summary>Set by the host to drive the Expand/Restore button label. Independent of GalleryViewModel.IsViewerExpanded.</summary>
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<InlineArtworkViewer, bool>(nameof(IsExpanded));
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty)
            ApplyExpandedState((bool)change.NewValue!);
    }

    private void ApplyExpandedState(bool expanded)
    {
        if (this.FindControl<Button>("BrowsePanelButton") is { } browse)
            browse.IsVisible = !expanded;
        if (this.FindControl<TextBlock>("ExpandLabel") is { } expandLbl)
            expandLbl.IsVisible = !expanded;
        if (this.FindControl<TextBlock>("RestoreLabel") is { } restoreLbl)
            restoreLbl.IsVisible = expanded;
    }

    private readonly PixivClient _pixivClient;
    private readonly PixivImageLoader _imageLoader;
    private readonly PixivDownloadService _downloader;
    private readonly Pixora.Core.Services.LocalFavoritesService _favorites;
    private readonly AiViewModel _aiVm;

    private IReadOnlyList<ArtworkPage> _pages = [];
    private int _currentPageIndex;
    private ArtworkCardViewModel? _currentCard;
    private CancellationTokenSource? _loadCts;

    // Zoom / pan state
    private double _scale = 1.0;
    private double _translateX;
    private double _translateY;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartX;
    private double _panStartY;

    // Full-res loading
    private const double FullResThreshold = 1.5;
    private bool _fullResLoaded;
    private string? _currentOriginalUrl;

    public InlineArtworkViewer()
    {
        InitializeComponent();
        _pixivClient = AppServices.Get<PixivClient>();
        _imageLoader = AppServices.Get<PixivImageLoader>();
        _downloader = AppServices.Get<PixivDownloadService>();
        _favorites  = AppServices.Get<Pixora.Core.Services.LocalFavoritesService>();
        _aiVm       = AppServices.Get<AiViewModel>();

        // Bind message list once — never reset to avoid streaming race crashes
        Loaded += (_, _) =>
        {
            if (AiMessagesList != null && AiMessagesList.ItemsSource == null)
                AiMessagesList.ItemsSource = _aiVm.Messages;
            // Hook extent changes so scroll fires after layout whenever content grows
            if (AiScrollViewer != null)
                AiScrollViewer.PropertyChanged += OnScrollViewerPropertyChanged;
        };

        _aiVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AiViewModel.StatusText) && AiStatusLabel != null)
                Dispatcher.UIThread.Post(() => AiStatusLabel.Text = _aiVm.StatusText);
            if (e.PropertyName == nameof(AiViewModel.IsThinking))
                RefreshAiMessages();
        };
        // On new message: hook content streaming for auto-scroll, then refresh
        _aiVm.Messages.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is AiChatMessage msg)
                        msg.PropertyChanged += (_, _) => ScrollToBottomDeferred();
                }
            }
            RefreshAiMessages();
        };

        DataContextChanged += OnDataContextChanged;
    }

    private GalleryViewModel? VM => DataContext as GalleryViewModel;

    /// <summary>The tab collection shown in the strip — always the global ViewerTabs.</summary>
    private IEnumerable<ViewerTab> ActiveTabs => VM?.ViewerTabs ?? Enumerable.Empty<ViewerTab>();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (VM is not { } vm) return;
        vm.PropertyChanged += OnVmPropertyChanged;

        // Always force reload on re-attach — _currentCard may match a stale instance
        _currentCard = null;
        _currentOriginalUrl = null;

        if (vm.InlineViewerCard != null)
        {
            _ = LoadCardAsync(vm.InlineViewerCard);
            UpdateTabHighlight();
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GalleryViewModel.InlineViewerCard))
        {
            _ = LoadCardAsync(VM?.InlineViewerCard);
            UpdateArtistButtonVisibility();
        }
        if (e.PropertyName == nameof(GalleryViewModel.SelectedViewerTab))
        {
            // Invalidate current card so LoadCardAsync always reloads when switching tabs
            _currentCard = null;
            _ = LoadCardAsync(VM?.InlineViewerCard);
            UpdateTabHighlight();
            UpdateArtistButtonVisibility();
        }
        if (e.PropertyName == nameof(GalleryViewModel.SelectedArtist))
            UpdateArtistButtonVisibility();
        if (e.PropertyName == nameof(GalleryViewModel.NavListVersion))
            UpdateArtworkCounter();
    }

    /// <summary>Keep the toolbar "👤 Artist" button hidden when already viewing this artist.</summary>
    private void UpdateArtistButtonVisibility()
    {
        if (GoToArtistBtn != null)
            GoToArtistBtn.IsVisible = !IsViewingCurrentArtist();
    }

    private void UpdateTabHighlight()
    {
        if (VM == null) return;
        var strip = this.FindControl<ItemsControl>("TabStrip");
        if (strip == null) return;
        var active = VM.SelectedViewerTab;
        foreach (var border in strip.GetVisualDescendants().OfType<Border>()
                     .Where(b => b.Name == "TabItem"))
        {
            bool isActive = border.DataContext == active;
            border.Opacity = isActive ? 1.0 : 0.55;
        }
    }

    private void OnTabListClick(object? sender, RoutedEventArgs e)
    {
        if (VM == null || sender is not Button btn) return;
        var menu = new ContextMenu();
        foreach (var tab in ActiveTabs)
        {
            var item = new MenuItem { Header = tab.Header };
            var captured = tab;
            item.Click += (_, _) => VM.SelectedViewerTab = captured;
            if (tab == VM.SelectedViewerTab)
                item.Classes.Add("accent");
            menu.Items.Add(item);
        }
        menu.Open(btn);
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border b && b.DataContext is ViewerTab tab && VM != null)
            VM.SelectedViewerTab = tab;
    }

    private void OnTabCloseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            var border = btn.FindAncestorOfType<Border>();
            if (border?.DataContext is ViewerTab tab && VM != null)
                VM.CloseViewerTabCommand.Execute(tab);
        }
        e.Handled = true;
    }

    private async Task LoadCardAsync(ArtworkCardViewModel? card)
    {
        if (card == null) return;

        // Cancel any in-flight load
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var ct = cts.Token;

        _currentCard = card;
        _aiVm.CurrentImageBytes = null;
        // Switch to (or create) the per-artwork Hoshi session for this card
        _aiVm.SwitchToArtworkSession(card);
        _currentPageIndex = 0;
        _pages = [];
        _fullResLoaded = false;
        UpdateFollowButton();
        UpdateFavoriteButton(card);
        _currentOriginalUrl = null;

        UpdatePageIndicator();
        UpdateArtworkCounter();
        SetLoading(true);
        ResetZoom();

        try
        {
            var pages = await _pixivClient.GetArtworkPagesAsync(card.Id);
            if (ct.IsCancellationRequested) return; // stale — a newer tab won
            _pages = pages;
            UpdatePageIndicator();
            await RenderPageAsync(_currentPageIndex, ct);
        }
        catch (OperationCanceledException) { /* expected on tab switch */ }
        catch { /* non-fatal */ }
        finally { if (!ct.IsCancellationRequested) SetLoading(false); }

        // Tags now displayed via ItemsControl binding to InlineViewerCard.Tags
    }

    private async Task RenderPageAsync(int index, CancellationToken ct = default)
    {
        if (_pages.Count == 0 || index < 0 || index >= _pages.Count) return;
        SetLoading(true);

        _fullResLoaded = false;
        _currentOriginalUrl = _pages[index].Urls.Original;
        var url = _pages[index].Urls.Regular ?? _pages[index].Urls.Small
               ?? _pages[index].Urls.Original ?? _pages[index].Urls.ThumbMini;

        if (!string.IsNullOrEmpty(url))
        {
            var bytes = await _imageLoader.FetchBytesAsync(url);
            if (ct.IsCancellationRequested) return;
            if (bytes != null)
            {
                // Store for AI vision queries
                _aiVm.CurrentImageBytes = bytes;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ct.IsCancellationRequested) return;
                    using var ms = new MemoryStream(bytes);
                    ViewerImage.Source = new Bitmap(ms);
                    ResetZoom();
                });
            }
        }
        SetLoading(false);
    }

    private void SetLoading(bool loading)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (LoadingPanel != null) LoadingPanel.IsVisible = loading;
            if (ViewerImage != null) ViewerImage.IsVisible = !loading;
        });
    }

    private void UpdatePageIndicator()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var total = _pages.Count > 0 ? _pages.Count : _currentCard?.PageCount ?? 1;
            var current = _currentPageIndex + 1;
            var isMulti = total > 1;

            if (PageIndicatorText != null)
                PageIndicatorText.Text = $"{current} / {total}";

            // Update context menu labels with real page counts
            if (CtxDlPageHeader != null)
                CtxDlPageHeader.Text = isMulti
                    ? $"↓ Download page {current} of {total}"
                    : "↓ Download this image";
            if (CtxDlAll != null)
                CtxDlAll.IsVisible = isMulti;
            if (CtxDlAllHeader != null)
                CtxDlAllHeader.Text = $"↓ Download all {total} pages";
            if (CtxDlRange != null)
                CtxDlRange.IsVisible = isMulti;
            if (CtxDlRangeHeader != null)
                CtxDlRangeHeader.Text = $"↓ Download page range… (1–{total})";

            // Drive the visible action-bar buttons from the real page count too —
            // pre-load metadata can be wrong, so override the XAML IsVisible binding.
            var prev = this.FindControl<Button>("PrevPageBtn");
            var next = this.FindControl<Button>("NextPageBtn");
            var dlAll = this.FindControl<Button>("DlAllBtn");
            var dlRange = this.FindControl<Button>("DlRangeBtn");
            if (prev != null) prev.IsVisible = isMulti;
            if (next != null) next.IsVisible = isMulti;
            if (dlAll != null) dlAll.IsVisible = isMulti;
            if (dlRange != null) dlRange.IsVisible = isMulti;
        });
    }

    // ── Zoom/pan ────────────────────────────────────────────────────────────

    private void ApplyTransform()
    {
        if (ViewerImage.RenderTransform is TransformGroup tg)
        {
            if (tg.Children[0] is ScaleTransform s) { s.ScaleX = _scale; s.ScaleY = _scale; }
            if (tg.Children[1] is TranslateTransform t) { t.X = _translateX; t.Y = _translateY; }
        }
        if (ZoomLabel != null) ZoomLabel.Text = $"{_scale * 100:0}%";
        if (_scale >= FullResThreshold && !_fullResLoaded && !string.IsNullOrEmpty(_currentOriginalUrl))
            _ = LoadFullResAsync(_currentOriginalUrl!);
    }

    private async Task LoadFullResAsync(string originalUrl)
    {
        _fullResLoaded = true;
        var bytes = await _imageLoader.FetchBytesAsync(originalUrl);
        if (bytes == null || _currentOriginalUrl != originalUrl) return;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            using var ms = new MemoryStream(bytes);
            ViewerImage.Source = new Bitmap(ms);
        });
    }

    private void ResetZoom()
    {
        _scale = 1.0; _translateX = 0; _translateY = 0;
        ApplyTransform();
    }

    private void OnImageWheel(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        var pos = e.GetPosition(ImageCanvas);
        // Zoom towards the cursor
        _translateX = pos.X - (pos.X - _translateX) * delta;
        _translateY = pos.Y - (pos.Y - _translateY) * delta;
        _scale = Math.Clamp(_scale * delta, 0.1, 10.0);
        ApplyTransform();
        e.Handled = true;
    }

    private void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        _isPanning = true;
        _panStart = e.GetPosition(ImageCanvas);
        _panStartX = _translateX;
        _panStartY = _translateY;
        e.Handled = true;
    }

    private void OnImagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPanning) return;
        var pos = e.GetPosition(ImageCanvas);
        _translateX = _panStartX + (pos.X - _panStart.X);
        _translateY = _panStartY + (pos.Y - _panStart.Y);
        ApplyTransform();
    }

    private void OnImagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPanning = false;
    }

    // ── Controls ─────────────────────────────────────────────────────────────

    private void ZoomAroundCenter(double factor)
    {
        if (ImageCanvas == null) return;
        var cx = ImageCanvas.Bounds.Width  / 2;
        var cy = ImageCanvas.Bounds.Height / 2;
        _translateX = cx - (cx - _translateX) * factor;
        _translateY = cy - (cy - _translateY) * factor;
        _scale = Math.Clamp(_scale * factor, 0.1, 10.0);
        ApplyTransform();
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e)  => ZoomAroundCenter(1.25);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => ZoomAroundCenter(1.0 / 1.25);
    private void OnZoomFit(object? sender, RoutedEventArgs e) => ResetZoom();

    private void OnPrevPage(object? sender, RoutedEventArgs e)
    {
        if (_currentPageIndex > 0)
        {
            _currentPageIndex--;
            UpdatePageIndicator();
            _ = RenderPageAsync(_currentPageIndex);
        }
    }

    private void OnNextPage(object? sender, RoutedEventArgs e)
    {
        if (_currentPageIndex < _pages.Count - 1)
        {
            _currentPageIndex++;
            UpdatePageIndicator();
            _ = RenderPageAsync(_currentPageIndex);
        }
    }

    private void OnDownloadCurrentPage(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null || VM == null) return;
        _ = VM.DownloadSinglePageAsync(_currentCard, _currentPageIndex);
    }

    private void OnDownloadAllPages(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null || VM == null) return;
        _ = VM.DownloadSingleAsync(_currentCard);
    }

    private async void OnDownloadPageRange(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null || VM == null) return;
        if (_pages.Count <= 1) { OnDownloadCurrentPage(sender, e); return; }
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;

        var dialog = new RangePickerDialog(
            title: $"Page range — {_currentCard.Title}",
            description: $"Artwork has {_pages.Count} pages (0-based). Examples: \"0-2\", \"0,3,5\".",
            maxInclusive: _pages.Count - 1,
            placeholder: $"0-{_pages.Count - 1}");
        var ok = await dialog.ShowDialog<bool?>(window);
        if (ok == true && dialog.SelectedIndexes.Count > 0)
            _ = VM.DownloadPagesAsync(_currentCard, dialog.SelectedIndexes);
    }

    private async void OnOpenPopup(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null || VM == null) return;
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;
        var viewer = new ArtworkViewerWindow(_currentCard.Artwork, VM);
        await viewer.ShowDialog(window);
    }

    private void OnOpenInPixiv(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null) return;
        var url = $"https://www.pixiv.net/artworks/{_currentCard.Id}";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // Use the active tab's nav list, then InlineViewerCardList, then FilteredArtworks.
    private System.Collections.Generic.IReadOnlyList<ArtworkCardViewModel> NavList()
    {
        if (VM?.SelectedViewerTab is { } tab && tab.NavList.Count > 0)
            return tab.NavList;
        if (VM?.InlineViewerCardList is { } ext)
            return ext;
        return VM?.FilteredArtworks
            ?? (System.Collections.Generic.IReadOnlyList<ArtworkCardViewModel>)System.Array.Empty<ArtworkCardViewModel>();
    }

    private void OnCloseAllClicked(object? sender, RoutedEventArgs e)
    {
        VM?.CloseInlineViewerCommand.Execute(null);
        RaiseEvent(new RoutedEventArgs(ViewerClosedEvent, this));
    }

    private void OnBrowseButtonClicked(object? sender, RoutedEventArgs e)
    {
        var args = new RoutedEventArgs(ToggleBrowseEvent, this);
        RaiseEvent(args);
        if (!args.Handled)
            VM?.TogglePreviewCommand.Execute(null);
    }

    private void OnExpandButtonClicked(object? sender, RoutedEventArgs e)
    {
        var args = new RoutedEventArgs(ExpandViewerEvent, this);
        RaiseEvent(args);
        if (!args.Handled && VM != null)
            VM.IsViewerExpanded = !VM.IsViewerExpanded;
    }

    private void OnPrevArtwork(object? sender, RoutedEventArgs e)
    {
        if (VM == null || _currentCard == null) return;
        var list = NavList();
        var idx = IndexOfById(list, _currentCard.Id);
        if (idx <= 0) return;
        var next = list[idx - 1];
        if (VM.SelectedViewerTab is { } tab)
        {
            tab.NavigateTo(next);
            _currentCard = null; // force reload
            _ = LoadCardAsync(next);
            VM.InlineViewerCard = next;
        }
        else
            VM.OpenInlineViewer(next);
    }

    private void OnNextArtwork(object? sender, RoutedEventArgs e)
    {
        if (VM == null || _currentCard == null) return;
        var list = NavList();
        var idx = IndexOfById(list, _currentCard.Id);
        if (idx < 0) return;

        // If we're at the end of the loaded list but more exist, trigger a background load
        if (idx >= list.Count - 1)
        {
            if (VM.SelectedViewerTab is { LoadMoreAsync: not null } loadTab)
                _ = LoadMoreIntoTabAsync(loadTab);
            return;
        }

        var next = list[idx + 1];
        if (VM.SelectedViewerTab is { } tab)
        {
            tab.NavigateTo(next);
            _currentCard = null;
            _ = LoadCardAsync(next);
            VM.InlineViewerCard = next;
        }
        else
            VM.OpenInlineViewer(next);
    }

    private async Task LoadMoreIntoTabAsync(ViewerTab tab)
    {
        if (tab.LoadMoreAsync == null) return;
        if (NextArtworkBtn != null) NextArtworkBtn.IsEnabled = false;
        try
        {
            var newCards = await tab.LoadMoreAsync();
            // Extend the tab's nav list with any cards not already present
            var existingIds = new System.Collections.Generic.HashSet<string>(tab.NavList.Select(c => c.Id));
            foreach (var c in newCards)
                if (existingIds.Add(c.Id)) tab.NavList.Add(c);
            UpdateArtworkCounter();
        }
        catch { }
    }

    private static int IndexOfById(System.Collections.Generic.IReadOnlyList<ArtworkCardViewModel> list, string id)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i].Id == id) return i;
        return -1;
    }

    private void UpdateArtworkCounter()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (VM == null || _currentCard == null || ArtworkCounterLabel == null) return;
            var list = NavList();
            var idx = IndexOfById(list, _currentCard.Id);
            if (idx < 0)
            {
                ArtworkCounterLabel.Text = "";
                if (PrevArtworkBtn != null) PrevArtworkBtn.IsEnabled = false;
                if (NextArtworkBtn != null) NextArtworkBtn.IsEnabled = false;
                return;
            }
            // Use the tab's true total (full artist catalogue) if available
            var tab = VM.SelectedViewerTab;
            var total = (tab != null && tab.TotalCount > list.Count) ? tab.TotalCount : list.Count;
            ArtworkCounterLabel.Text = $"{idx + 1} / {total}";
            if (PrevArtworkBtn != null) PrevArtworkBtn.IsEnabled = idx > 0;
            // Can go next if not at loaded end, or if more can be loaded from source
            var canGoNext = idx < list.Count - 1 || (tab?.LoadMoreAsync != null);
            if (NextArtworkBtn != null) NextArtworkBtn.IsEnabled = canGoNext;
        });
    }

    private void OnGoToArtist(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null) return;
        NavigateToArtistGallery(_currentCard.UserId);
    }

    private void NavigateToArtistGallery(string userId)
    {
        // Navigate to Gallery tab and load the artist — keep existing tabs open
        var mainWindow = TopLevel.GetTopLevel(this) as Pixora.Avalonia.Views.MainWindow;
        var galleryVm = AppServices.Get<GalleryViewModel>();
        // Close the inline viewer only if no tabs are pinned
        if (galleryVm.ViewerTabs.Count == 0)
            galleryVm.CloseInlineViewer();
        mainWindow?.LoadGalleryView();
        _ = galleryVm.LoadArtistByIdCommand.ExecuteAsync(userId);
    }

    private void OnFollowToggleClicked(object? sender, RoutedEventArgs e)
    {
        // Follow/unfollow feature removed - Pixiv OAuth no longer available
        if (VM != null)
        {
            VM.StatusMessage = "Follow/unfollow is not available. Pixiv has blocked OAuth authentication.";
        }
    }

    private void UpdateFollowButton()
    {
        // Follow button hidden - feature unavailable due to Pixiv OAuth restrictions
        if (FollowToggleBtn != null)
            FollowToggleBtn.IsVisible = false;
    }

    private void OnOpenArtistInPixiv(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null) return;
        var url = $"https://www.pixiv.net/users/{_currentCard.UserId}";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private void OnCopyId(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null) return;
        var cb = TopLevel.GetTopLevel(this)?.Clipboard;
        if (cb == null) return;
        var dt = new global::Avalonia.Input.DataTransfer();
        dt.Add(global::Avalonia.Input.DataTransferItem.CreateText(_currentCard.Id));
        _ = cb.SetDataAsync(dt);
    }

    private void OnCopyImage(object? sender, RoutedEventArgs e)
    {
        if (ViewerImage.Source is not global::Avalonia.Media.Imaging.Bitmap bmp) return;
        var cb = TopLevel.GetTopLevel(this)?.Clipboard;
        if (cb == null) return;
        _ = cb.SetBitmapAsync(bmp);
    }

    private void OnTagPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (sender is not Border border) return;
        if (border.DataContext is not string tag) return;
        if (DataContext is not GalleryViewModel vm) return;

        e.Handled = true;
        // The inline viewer may be hosted inside Discover/Rankings — ensure we
        // switch to the Gallery tab so the search results are actually visible.
        if (TopLevel.GetTopLevel(this) is Pixora.Avalonia.Views.MainWindow main)
            main.LoadGalleryView();

        // Shift+click = global Pixiv search; regular click = filter within current artist
        bool isShiftPressed = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;

        if (isShiftPressed)
        {
            // Global Pixiv search
            vm.CloseInlineViewer();
            if (vm.SearchByTagCommand.CanExecute(tag))
                _ = vm.SearchByTagCommand.ExecuteAsync(tag);
        }
        else
        {
            // Filter within current artist's gallery
            vm.CloseInlineViewer();
            vm.TagIncludeFilter = tag;
            vm.ShowFilters = true;
        }
    }

    private void OnTagOpenArtistGallery(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null) return;
        NavigateToArtistGallery(_currentCard.UserId);
    }

    /// <summary>
    /// True when the gallery already has this card's artist selected — used to
    /// hide the "open artist gallery" affordances since they would be a no-op.
    /// </summary>
    private bool IsViewingCurrentArtist()
    {
        if (_currentCard == null) return false;
        var galleryVm = DataContext as GalleryViewModel ?? AppServices.Get<GalleryViewModel>();
        return galleryVm.SelectedArtist?.UserId == _currentCard.UserId;
    }

    private void OnImageContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var hide = IsViewingCurrentArtist();
        if (GoToArtistMenuItem != null) GoToArtistMenuItem.IsVisible = !hide;
        // Toolbar button mirrors the menu item — keep them in sync
        if (GoToArtistBtn != null) GoToArtistBtn.IsVisible = !hide;
    }

    private void OnTagContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        var hide = IsViewingCurrentArtist();
        foreach (var item in menu.Items)
        {
            if (item is MenuItem mi && (mi.Tag as string) == "OpenArtistGallery")
            {
                mi.IsVisible = !hide;
                break;
            }
        }
    }

    private void OnTagSearchGallery(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        if (mi.Parent is not ContextMenu cm) return;
        if (cm.PlacementTarget is not Border border) return;
        if (border.DataContext is not string tag) return;
        if (DataContext is not GalleryViewModel vm) return;

        vm.CloseInlineViewer();
        vm.TagIncludeFilter = tag;
        vm.ShowFilters = true;
    }

    private void OnTagSearchPixiv(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        if (mi.Parent is not ContextMenu cm) return;
        if (cm.PlacementTarget is not Border border) return;
        if (border.DataContext is not string tag) return;

        var url = $"https://www.pixiv.net/tags/{Uri.EscapeDataString(tag)}/artworks";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    // ── Local favorite ──────────────────────────────────────────────────────

    private void UpdateFavoriteButton(ArtworkCardViewModel? card)
    {
        if (LocalFavBtn == null || LocalFavLabel == null) return;
        var isFav = card != null && _favorites.IsFavorite(card.Id);
        LocalFavLabel.Text = isFav ? "★ Favorited" : "☆ Favorite";
        if (card != null) card.IsLocalFavorite = isFav;
    }

    private void OnToggleLocalFavorite(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null) return;
        _favorites.Toggle(_currentCard.Artwork);
        UpdateFavoriteButton(_currentCard);
    }

    // ── Hoshi (星) AI assistant ───────────────────────────────────────────

    private async void OnAiToggleClicked(object? sender, RoutedEventArgs e)
    {
        if (AiPanel == null) return;

        if (!_aiVm.IsEnabled)
        {
            // First enable: start Ollama + pull model
            if (AiToggleBtnLabel != null) AiToggleBtnLabel.Text = "Hoshi…";
            if (AiToggleBtn != null)      AiToggleBtn.IsEnabled = false;

            await _aiVm.ToggleEnabledAsync();

            if (AiToggleBtn != null) AiToggleBtn.IsEnabled = true;
            UpdateAiToggleButton();

            if (_aiVm.IsEnabled)
                AiPanel.IsVisible = true;
        }
        else
        {
            // Toggle panel open/close
            AiPanel.IsVisible = !AiPanel.IsVisible;
            if (AiPanel.IsVisible && AiInputBox != null)
                AiInputBox.Focus();
        }
        UpdateAiPanelRowSize();
        UpdateAiToggleButton();
        if (AiPanel.IsVisible) ScrollToBottomDeferred();
    }

    private void UpdateAiPanelRowSize()
    {
        // Row 5 is the AiPanel row — expand to * when visible, collapse to Auto when hidden
        if (RootGrid == null || AiPanel == null) return;
        RootGrid.RowDefinitions[5].Height = AiPanel.IsVisible
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;
    }

    private void UpdateAiToggleButton()
    {
        if (AiToggleBtnLabel == null) return;
        AiToggleBtnLabel.Text = _aiVm.IsEnabled
            ? (AiPanel?.IsVisible == true ? "Hoshi ▲" : "Hoshi ▼")
            : "Hoshi";
        if (AiStatusLabel != null)
            AiStatusLabel.Text = _aiVm.StatusText;
    }

    private void RefreshAiMessages()
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Ensure binding is set if Loaded fired before _aiVm was ready
            if (AiMessagesList != null && AiMessagesList.ItemsSource == null)
                AiMessagesList.ItemsSource = _aiVm.Messages;

            // Show thinking indicator in send button
            if (AiSendBtn != null)
                AiSendBtn.Content = _aiVm.IsThinking ? "…" : "Send";
        });
        ScrollToBottomDeferred();
    }

    private bool _autoScroll = true;

    private void OnScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.ExtentProperty && _autoScroll)
            AiScrollViewer?.ScrollToEnd();
        // If user scrolls up, disable auto-scroll; re-enable when they reach the bottom
        if (e.Property == ScrollViewer.OffsetProperty && AiScrollViewer != null)
        {
            var offset = AiScrollViewer.Offset.Y;
            var atBottom = AiScrollViewer.Extent.Height - offset - AiScrollViewer.Viewport.Height < 40;
            _autoScroll = atBottom;
        }
    }

    /// <summary>Scrolls the AI chat view to the bottom and re-enables auto-scroll.</summary>
    private void ScrollToBottomDeferred()
    {
        _autoScroll = true;
        Dispatcher.UIThread.Post(() => AiScrollViewer?.ScrollToEnd(),
            global::Avalonia.Threading.DispatcherPriority.Background);
    }

    private async void OnAiSendClicked(object? sender, RoutedEventArgs e)
    {
        if (AiInputBox == null || string.IsNullOrWhiteSpace(AiInputBox.Text)) return;
        _aiVm.InputText = AiInputBox.Text;
        AiInputBox.Text = string.Empty;
        await _aiVm.SendAsync();
    }

    private async void OnAiInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            await OnAiSendAsync();
        }
    }

    private async Task OnAiSendAsync()
    {
        if (AiInputBox == null || string.IsNullOrWhiteSpace(AiInputBox.Text)) return;
        _aiVm.InputText = AiInputBox.Text;
        AiInputBox.Text = string.Empty;
        await _aiVm.SendAsync();
    }

    private async void OnAiDescribeClicked(object? sender, RoutedEventArgs e)
        => await _aiVm.DescribeImageAsync();

    private async void OnAiTagsClicked(object? sender, RoutedEventArgs e)
        => await _aiVm.SuggestTagsAsync();

    private void OnAiFavClicked(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null) return;
        _favorites.Toggle(_currentCard.Artwork);
        UpdateFavoriteButton(_currentCard);
        var msg = _favorites.IsFavorite(_currentCard.Id)
            ? $"Added \"{_currentCard.Title}\" to local favorites ★"
            : $"Removed \"{_currentCard.Title}\" from favorites.";
        _aiVm.Messages.Add(new AiChatMessage { Role = "assistant", Content = msg });
        RefreshAiMessages();
    }

    private async void OnAiDlClicked(object? sender, RoutedEventArgs e)
    {
        if (_currentCard == null) return;
        var card = _currentCard;
        _aiVm.Messages.Add(new AiChatMessage { Role = "assistant", Content = $"⏳ Downloading \"{card.Title}\"…" });
        RefreshAiMessages();

        try
        {
            var paths = await _downloader.DownloadArtworkAsync(card.Artwork);
            if (paths.Count == 0)
            {
                _aiVm.Messages.Add(new AiChatMessage { Role = "assistant", Content = $"⚠ No files were downloaded for \"{card.Title}\"." });
            }
            else
            {
                var folder = System.IO.Path.GetDirectoryName(paths[0]) ?? "(unknown)";
                var fileWord = paths.Count == 1 ? "file" : "files";
                _aiVm.Messages.Add(new AiChatMessage
                {
                    Role = "assistant",
                    Content = $"✓ Downloaded {paths.Count} {fileWord} for \"{card.Title}\"\nSaved to: {folder}"
                });
            }
        }
        catch (Exception ex)
        {
            _aiVm.Messages.Add(new AiChatMessage { Role = "system", Content = $"✗ Download failed for \"{card.Title}\": {ex.Message}" });
        }

        RefreshAiMessages();
    }

    private void OnAiClearClicked(object? sender, RoutedEventArgs e)
        => _aiVm.ClearChat();
}

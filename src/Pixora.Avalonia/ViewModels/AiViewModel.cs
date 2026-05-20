using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pixora.Avalonia.Services;
using Pixora.Core.Data;
using Pixora.Core.Services;
using Pixora.Core.Settings;
using Pixora.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pixora.Avalonia.ViewModels;

/// <summary>A single message in the AI chat.</summary>
public partial class AiChatMessage : ObservableObject
{
    public string Role { get; init; } = "user";      // "user" | "assistant" | "system"
    [ObservableProperty] private string _content = string.Empty;
    public byte[]? ImageBytes { get; init; }         // Optional image to display inline
    public bool IsUser      => Role == "user";
    public bool IsAssistant => Role == "assistant";
    public bool IsSystem    => Role == "system";
    public bool HasImage    => ImageBytes != null && ImageBytes.Length > 0;
}

/// <summary>
/// ViewModel that backs the AI assistant panel.
/// Exposed as a singleton so both the viewer and the sidebar can bind to it.
/// </summary>
public partial class AiViewModel : ObservableObject
{
    private readonly OllamaService _ollama;
    private readonly LocalFavoritesService _favorites;
    private readonly PixivDownloadService _downloader;
    private readonly DownloadJobRepository _jobRepository;
    private readonly DownloadCoordinator _coordinator;
    private readonly HoshiSessionService _sessions;
    private readonly PixivClient _pixiv;
    private readonly PixivImageLoader _imageLoader;
    private readonly SettingsService _settings;
    private readonly ImageLookupService _imageLookup;

    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isThinking;
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string _statusText = "Hoshi is off";
    [ObservableProperty] private bool _isModelReady;

    /// <summary>The artwork card currently in the viewer — set by the host (inline viewer flow).</summary>
    public ArtworkCardViewModel? CurrentCard { get; set; }
    /// <summary>
    /// The current page thumbnail bytes used for vision queries.
    /// When a standalone session is active, this mirrors the session's image bytes.
    /// </summary>
    public byte[]? CurrentImageBytes
    {
        get => _currentImageBytes;
        set
        {
            if (_currentImageBytes == value) return;
            _currentImageBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
            // Cache image into active session so it persists across restarts
            if (!_suppressSave && CurrentSession is { } s && value is { Length: > 0 })
            {
                s.ImageBytes = value;
                _ = _sessions.SaveAsync(s);
            }
        }
    }
    private byte[]? _currentImageBytes;

    /// <summary>True when an image is attached for vision queries.</summary>
    public bool HasImage => _currentImageBytes is { Length: > 0 };

    /// <summary>The active standalone session, or null when bound to the inline viewer's transient context.</summary>
    [ObservableProperty] private HoshiSession? _currentSession;

    /// <summary>All persisted sessions (sorted most-recent-first).</summary>
    public ObservableCollection<HoshiSession> Sessions => _sessions.Sessions;

    /// <summary>Sessions grouped by date for the sidebar.</summary>
    public List<SessionGroupViewModel> GroupedSessions => _sessions.GetGroupedSessions();

    public ObservableCollection<AiChatMessage> Messages { get; } = [];

    private CancellationTokenSource? _cts;       // for send
    private CancellationTokenSource? _enableCts;  // for enable
    private bool _nextSendWithImage;              // true = next SendAsync attaches image bytes
    private bool _sessionAutoNamed;               // true = session title was already auto-set
    private bool _suppressSave;                   // true during LoadSession to avoid saving []

    public AiViewModel(
        OllamaService ollama,
        LocalFavoritesService favorites,
        PixivDownloadService downloader,
        DownloadJobRepository jobRepository,
        DownloadCoordinator coordinator,
        HoshiSessionService sessions,
        PixivClient pixiv,
        PixivImageLoader imageLoader,
        SettingsService settings,
        ImageLookupService imageLookup)
    {
        _ollama        = ollama;
        _favorites     = favorites;
        _downloader    = downloader;
        _jobRepository = jobRepository;
        _coordinator   = coordinator;
        _sessions      = sessions;
        _pixiv         = pixiv;
        _imageLoader   = imageLoader;
        _settings      = settings;
        _imageLookup   = imageLookup;

        _ollama.StateChanged += (_, _) => Dispatcher.UIThread.Post(SyncOllamaState);
        SyncOllamaState();

        // Restore Hoshi enabled state from settings
        if (_settings.Current.HoshiEnabled)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(500); // Small delay to ensure services are ready
                await _ollama.EnableAsync();
                Dispatcher.UIThread.Post(SyncOllamaState);
            });
        }

        // Auto-persist the current session whenever messages change
        Messages.CollectionChanged += (_, ce) =>
        {
            if (_suppressSave) return;
            // Subscribe to content changes on newly added messages (streaming)
            if (ce.NewItems != null)
            {
                foreach (var item in ce.NewItems)
                {
                    if (item is AiChatMessage msg)
                        msg.PropertyChanged += (_, _) => PersistCurrentSession();
                }
            }
            PersistCurrentSession();
        };
        
        // Notify when sessions collection changes to update grouped sessions
        _sessions.Sessions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(GroupedSessions));

        // When the sessions directory is swapped (account switch), clear the
        // current session/messages so we don't keep showing the previous account's chat.
        _sessions.SessionsChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _suppressSave = true;
                try
                {
                    CurrentSession    = null;
                    Messages.Clear();
                    CurrentImageBytes = null;
                    _sessionAutoNamed = false;
                    _ollama.ClearHistory();
                }
                finally { _suppressSave = false; }
                OnPropertyChanged(nameof(GroupedSessions));
            });
        };
    }

    // ── Sessions ──────────────────────────────────────────────────────────────

    /// <summary>Creates a new empty session and switches to it.</summary>
    public HoshiSession StartNewSession(string? title = null)
    {
        var s = _sessions.CreateNew(title);
        LoadSession(s);
        return s;
    }

    /// <summary>
    /// Switches to the existing session for <paramref name="artworkId"/>, or creates a new one.
    /// Called whenever the inline viewer loads a new artwork / tab.
    /// </summary>
    public void SwitchToArtworkSession(ArtworkCardViewModel card)
    {
        // Always track the current card so intents (identify, info, etc.) work even when disabled
        CurrentCard = card;

        // Only create/restore session if Hoshi is enabled
        if (!IsEnabled)
            return;

        // Save the current session before switching
        if (CurrentSession is { } prev)
        {
            prev.Messages = Messages.Select(m => new PersistedMessage { Role = m.Role, Content = m.Content }).ToList();
            _ = _sessions.SaveAsync(prev);
        }

        // Find an existing session for this artwork
        var existing = _sessions.Sessions.FirstOrDefault(s => s.PixivArtworkId == card.Id);
        if (existing != null)
        {
            LoadSession(existing);
        }
        else
        {
            // Create a fresh session pre-loaded with the artwork ID and title
            var s = _sessions.CreateNew(card.Title);
            s.PixivArtworkId = card.Id;
            s.ImageSource = $"pixiv:{card.Id}";
            _suppressSave = true;
            try
            {
                CurrentSession = s;
                _sessionAutoNamed = true; // title already set
                Messages.Clear();
                _ollama.ClearHistory();
            }
            finally { _suppressSave = false; }
            _ = _sessions.SaveAsync(s);
        }
    }

    /// <summary>Switches to an existing session: loads its image and messages into the view.</summary>
    public void LoadSession(HoshiSession session)
    {
        _suppressSave = true;
        try
        {
            CurrentSession = session;
            _sessionAutoNamed = session.Messages.Count > 0 || session.Title != "New chat";
            Messages.Clear();
            foreach (var m in session.Messages)
                Messages.Add(new AiChatMessage { Role = m.Role, Content = m.Content });
            CurrentImageBytes = session.ImageBytes;
            // Reset Ollama conversation history so the model starts fresh for this session
            _ollama.ClearHistory();
        }
        finally { _suppressSave = false; }
    }

    private void PersistCurrentSession()
    {
        if (CurrentSession is not { } s) return;
        s.Messages = Messages.Select(m => new PersistedMessage
        {
            Role = m.Role,
            Content = m.Content
        }).ToList();
        _ = _sessions.SaveAsync(s);
    }

    /// <summary>Explicitly saves the current session state.</summary>
    public async Task SaveCurrentSessionAsync()
    {
        if (CurrentSession is { } s)
        {
            s.Messages = Messages.Select(m => new PersistedMessage
            {
                Role = m.Role,
                Content = m.Content
            }).ToList();
            await _sessions.SaveAsync(s);
        }
    }

    /// <summary>Called when the Hoshi view is being unloaded/navigated away from.</summary>
    public async Task OnViewUnloadingAsync()
    {
        // Save the current session to prevent data loss
        await SaveCurrentSessionAsync();
    }

    /// <summary>Updates the current session's image and persists.</summary>
    public void SetSessionImage(byte[]? bytes, string? source = null, string? pixivArtworkId = null)
    {
        CurrentImageBytes = bytes;
        if (CurrentSession is { } s)
        {
            s.ImageBytes = bytes;
            s.ImageSource = source;
            if (pixivArtworkId != null) s.PixivArtworkId = pixivArtworkId;
            _ = _sessions.SaveAsync(s);
        }
    }

    /// <summary>Renames the current session and persists.</summary>
    public void RenameCurrentSession(string newTitle)
    {
        if (CurrentSession is not { } s) return;
        s.Title = string.IsNullOrWhiteSpace(newTitle) ? "Untitled" : newTitle.Trim();
        _ = _sessions.SaveAsync(s);
    }

    /// <summary>Deletes a session. If it was the current one, clears the view.</summary>
    public async Task DeleteSessionAsync(HoshiSession session)
    {
        var wasCurrent = CurrentSession?.Id == session.Id;
        await _sessions.DeleteAsync(session.Id);
        if (wasCurrent)
        {
            CurrentSession = null;
            Messages.Clear();
            CurrentImageBytes = null;
            _ollama.ClearHistory();
        }
    }

    /// <summary>Duplicates a session (preserves image + messages) and switches to the copy.</summary>
    public HoshiSession DuplicateSession(HoshiSession source)
    {
        var copy = _sessions.Duplicate(source);
        LoadSession(copy);
        return copy;
    }

    /// <summary>Deletes all sessions and clears the view.</summary>
    public async Task DeleteAllSessionsAsync()
    {
        var sessionIds = _sessions.Sessions.Select(s => s.Id).ToList();
        foreach (var id in sessionIds)
        {
            await _sessions.DeleteAsync(id);
        }
        CurrentSession = null;
        Messages.Clear();
        CurrentImageBytes = null;
        _ollama.ClearHistory();
    }

    // ── Toggle enable/disable ────────────────────────────────────────────────
    [RelayCommand]
    public Task ToggleEnabledAsync() => ToggleEnabledAsync(null);

    public async Task ToggleEnabledAsync(IProgress<string>? externalProgress)
    {
        if (IsEnabled)
        {
            Disable();
        }
        else
        {
            IsEnabled    = true;
            IsThinking   = true;
            StatusText   = "Starting…";

            var progress = new Progress<string>(msg =>
                Dispatcher.UIThread.Post(() => { StatusText = msg; externalProgress?.Report(msg); }));

            _enableCts?.Cancel();
            _enableCts = new CancellationTokenSource();
            await _ollama.EnableAsync(progress, _enableCts.Token);
            IsThinking = false;
            SyncOllamaState();

            if (_ollama.IsReady)
            {
                IsPanelOpen = true;
                AddSystemMessage("Hoshi 星 ready! I can describe images, suggest tags, download artwork, add to favorites, or move to a folder. What would you like to do?");
                
                // Save enabled state to settings
                _settings.Update(s => s.HoshiEnabled = true);
            }
        }
    }

    public void Disable()
    {
        _ollama.Disable();
        IsEnabled    = false;
        IsModelReady = false;
        IsPanelOpen  = false;
        
        // Save disabled state to settings
        _settings.Update(s => s.HoshiEnabled = false);
    }

    // ── Open/close panel without toggling enable ─────────────────────────────
    [RelayCommand]
    public void TogglePanel() => IsPanelOpen = IsEnabled && !IsPanelOpen;

    // ── Send message ─────────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendAsync()
    {
        var text = InputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        InputText = string.Empty;
        AddUserMessage(text);

        // Auto-create a session on first message if none exists (e.g. inline viewer)
        if (CurrentSession == null)
        {
            var s = _sessions.CreateNew();
            if (CurrentCard != null)
            {
                s.PixivArtworkId = CurrentCard.Id;
                if (CurrentImageBytes is { Length: > 0 } img)
                {
                    s.ImageBytes = img;
                    s.ImageSource = $"pixiv:{CurrentCard.Id}";
                }
            }
            CurrentSession = s;
            _sessionAutoNamed = false;
        }

        // Auto-name the session from the first user message
        if (CurrentSession is { } sess && !_sessionAutoNamed)
        {
            _sessionAutoNamed = true;
            var autoTitle = CurrentCard != null
                ? CurrentCard.Title
                : (text.Length > 40 ? text[..40].TrimEnd() + "…" : text);
            if (!string.IsNullOrWhiteSpace(autoTitle) && sess.Title == "New chat")
                RenameCurrentSession(autoTitle);
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsThinking = true;

        try
        {
            // Check for quick-action intents before sending to model
            if (await TryHandleIntentAsync(text, ct))
            {
                IsThinking = false;
                return;
            }

            // Stream response — add bubble only after first token arrives
            AiChatMessage? assistantMsg = null;

            // Attach image when explicitly requested OR when an image is attached and
            // the message sounds like it's about the image (avoids sending it for every chat msg).
            var hasImage = CurrentImageBytes?.Length > 0;
            var lowerText = text.ToLowerInvariant();
            var looksImageRelated = hasImage && (
                lowerText.Contains("image") || lowerText.Contains("picture") ||
                lowerText.Contains("photo") || lowerText.Contains("artwork") ||
                lowerText.Contains("artist") || lowerText.Contains("art") ||
                lowerText.Contains("draw") || lowerText.Contains("paint") ||
                lowerText.Contains("character") || lowerText.Contains("style") ||
                lowerText.Contains("tag") || lowerText.Contains("this") ||
                lowerText.Contains("her") || lowerText.Contains("him") ||
                lowerText.Contains("they") || lowerText.Contains("it") ||
                lowerText.Contains("show") || lowerText.Contains("look") ||
                lowerText.Contains("see") || lowerText.Contains("what") ||
                lowerText.Contains("who") || lowerText.Contains("how") ||
                lowerText.Contains("color") || lowerText.Contains("colour") ||
                lowerText.Contains("nsfw") || lowerText.Contains("r-18") ||
                lowerText.Contains("background") || lowerText.Contains("scene") ||
                lowerText.Contains("text") || lowerText.Contains("japanese") ||
                Messages.Count <= 4  // always include for early messages in a session
            );
            var useImage = (_nextSendWithImage || looksImageRelated) && hasImage;
            _nextSendWithImage = false;
            var stream = useImage
                ? _ollama.ChatWithImageAsync(text, CurrentImageBytes!, ct)
                : _ollama.ChatAsync(text, ct);

            await foreach (var chunk in stream.WithCancellation(ct))
            {
                if (ct.IsCancellationRequested) break;
                if (assistantMsg == null)
                {
                    assistantMsg = new AiChatMessage { Role = "assistant", Content = "" };
                    Messages.Add(assistantMsg);
                }
                assistantMsg.Content += chunk;
            }

            // If model returned nothing, show a fallback
            if (assistantMsg == null && !ct.IsCancellationRequested)
                AddAssistantMessage("…");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AddSystemMessage($"Error: {ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsThinking = false);
            // Save session after response completes
            await SaveCurrentSessionAsync();
        }
    }

    private bool CanSend() => IsModelReady && !IsThinking && !string.IsNullOrWhiteSpace(InputText);

    /// <summary>Marks the next SendAsync to include the current image bytes (vision query).</summary>
    public void RequestImageSend() => _nextSendWithImage = true;

    // Helper methods for image fetching intents
    private async Task<ArtworkCardViewModel?> FetchArtworkByIdAsync(string id)
    {
        if (!ulong.TryParse(id, out var artworkId))
            return null;

        // Use the existing metadata fetching method
        var metadata = await _pixiv.GetArtworksMetadataAsync(null, new List<string> { artworkId.ToString() });
        if (!metadata.TryGetValue(artworkId.ToString(), out var preview))
            return null;
        
        var card = new ArtworkCardViewModel(preview);
        return card;
    }

    private async Task<ArtworkCardViewModel?> FetchRandomByArtistAsync(string artistIdentifier, bool useRecent = false)
    {
        // Try to parse as artist ID first, then search by name
        string? artistId = null;
        if (ulong.TryParse(artistIdentifier, out var parsedId))
        {
            artistId = parsedId.ToString();
        }
        else
        {
            // Search for user by name
            var users = await _pixiv.SearchArtistsAsync(artistIdentifier);
            var first = users?.FirstOrDefault();
            if (first != null)
                artistId = first.UserId;
        }

        if (string.IsNullOrEmpty(artistId))
            return null;

        // Get user's artworks
        var artworksResponse = await _pixiv.GetUserIllustsAsync(artistId, 0, 48, CancellationToken.None);
        var artworks = artworksResponse?.Illusts ?? new List<ArtworkPreview>();
        if (!artworks.Any())
            return null;

        // Pick random or most recent
        var selected = useRecent ? artworks.First() : artworks[new Random().Next(artworks.Count)];
        var card = new ArtworkCardViewModel(selected);
        return card;
    }

    private async Task<ArtworkCardViewModel?> FetchRandomArtworkAsync()
    {
        // Use discovery artworks and pick random
        var discovery = await _pixiv.GetDiscoveryArtworksAsync();
        if (discovery?.Thumbnails?.Illust == null || !discovery.Thumbnails.Illust.Any())
            return null;

        var selected = discovery.Thumbnails.Illust[new Random().Next(discovery.Thumbnails.Illust.Count)];
        
        // Get the artwork ID and fetch full metadata
        if (ulong.TryParse(selected.Id, out var artworkId))
        {
            return await FetchArtworkByIdAsync(artworkId.ToString());
        }
        
        return null;
    }

    // ── Quick action buttons ─────────────────────────────────────────────────
    [RelayCommand]
    public async Task DescribeImageAsync()
    {
        _nextSendWithImage = true;
        InputText = "Describe this image in detail. Include the art style, subject, mood, and any notable elements.";
        await SendAsync();
    }

    [RelayCommand]
    public async Task SuggestTagsAsync()
    {
        _nextSendWithImage = true;
        InputText = "Suggest Pixiv-style tags for this image. Include both Japanese and English tags (Pixiv tags are usually Japanese). Example format: アニメ (anime), 女の子 (girl), ポートレート (portrait), 金髪 (blonde hair), 夕日 (sunset).";
        await SendAsync();
    }

    [RelayCommand]
    public async Task AskR18Async()
    {
        _nextSendWithImage = true;
        InputText = "Is this image NSFW or R-18? Answer yes or no and briefly explain why.";
        await SendAsync();
    }

    [RelayCommand]
    public void ClearChat()
    {
        Messages.Clear();
        _ollama.ClearHistory();
        if (_ollama.IsReady)
            AddSystemMessage("Chat cleared.");
    }

    /// <summary>
    /// Downloads an artwork using the DownloadJob pipeline so it appears in History
    /// (Active → Completed/Failed) just like downloads triggered from the artwork viewer.
    /// Reports progress as chat messages instead of failing silently.
    /// </summary>
    public async Task DownloadArtworkWithJobAsync(ArtworkCardViewModel card, CancellationToken ct = default)
    {
        AddAssistantMessage($"⏳ Downloading \"{card.Title}\"…");

        var target = new DownloadTarget
        {
            TargetId     = card.Artwork.Id,
            Name         = card.Artwork.Title,
            ThumbnailUrl = card.Artwork.ThumbnailUrl,
            UserName     = card.Artwork.UserName,
            UserId       = card.Artwork.UserId,
            Type         = TargetType.Artwork,
            Status       = TargetStatus.Running,
        };
        var job = new DownloadJob
        {
            Name      = card.Artwork.Title,
            Type      = DownloadJobType.ImageId,
            Status    = JobStatus.Running,
            StartedAt = DateTime.UtcNow,
            Targets   = [target],
        };

        await _jobRepository.SaveJobAsync(job);
        Console.Error.WriteLine($"[Hoshi] NotifyJobStarted: {job.Id} '{job.Name}'");
        _coordinator.NotifyJobStarted(job);
        await Task.Delay(50); // let HistoryViewModel's UI-thread Post run before download begins

        try
        {
            var paths = await _downloader.DownloadArtworkAsync(card.Artwork, ct: ct);
            target.Status = TargetStatus.Completed;
            target.DownloadedItems = paths.Count;
            job.Status = JobStatus.Completed;
            job.OutputFolder = paths.Count > 0 ? System.IO.Path.GetDirectoryName(paths[0]) : null;

            if (paths.Count == 0)
            {
                AddAssistantMessage($"⚠ No files were downloaded for \"{card.Title}\".");
            }
            else
            {
                var folder = job.OutputFolder ?? "(unknown)";
                var fileWord = paths.Count == 1 ? "file" : "files";
                AddAssistantMessage($"✓ Downloaded {paths.Count} {fileWord} for \"{card.Title}\"\nSaved to: {folder}");
            }
        }
        catch (OperationCanceledException)
        {
            target.Status = TargetStatus.Cancelled;
            job.Status = JobStatus.Cancelled;
        }
        catch (Exception ex)
        {
            target.Status = TargetStatus.Failed;
            target.ErrorMessage = ex.Message;
            job.Status = JobStatus.Failed;
            AddSystemMessage($"✗ Download failed for \"{card.Title}\": {ex.Message}");
        }
        finally
        {
            job.CompletedAt = DateTime.UtcNow;
            await _jobRepository.SaveJobAsync(job);
            Console.Error.WriteLine($"[Hoshi] NotifyJobSaved: {job.Id} status={job.Status}");
            _coordinator.NotifyJobSaved(job);
        }
    }

    // ── Intent detection: handle known commands without calling the model ────
    private async Task<bool> TryHandleIntentAsync(string input, CancellationToken ct)
    {
        var lower = input.ToLowerInvariant();

        // Download
        if (lower.Contains("download"))
        {
            ArtworkCardViewModel? card = CurrentCard;
            
            // If no current card, try to find the most recent artwork from messages
            if (card == null)
            {
                var lastImageMsg = Messages.LastOrDefault(m => m.HasImage && m.IsAssistant);
                if (lastImageMsg != null)
                {
                    // Try to extract artwork info from the message content
                    // This is a simple approach - in a real implementation we might store the artwork reference
                    AddAssistantMessage("⚠ No image selected for download. Please select an image first or use a specific image ID.");
                    return true;
                }
            }
            
            if (card != null)
            {
                await DownloadArtworkWithJobAsync(card, ct);
                return true;
            }
            else
            {
                AddAssistantMessage("⚠ No image available to download. Please fetch an image first using commands like 'show image by ID', 'random artwork', or 'random artist [name]'.");
                return true;
            }
        }

        // Add to favorites
        if ((lower.Contains("favorite") || lower.Contains("favourite")) && lower.Contains("add") && CurrentCard != null)
        {
            if (!_favorites.IsFavorite(CurrentCard.Id))
            {
                _favorites.Add(CurrentCard.Artwork);
                AddAssistantMessage($"Added \"{CurrentCard.Title}\" to local favorites ★");
            }
            else
            {
                AddAssistantMessage($"\"{CurrentCard.Title}\" is already in your local favorites.");
            }
            return true;
        }

        // Remove from favorites
        if ((lower.Contains("favorite") || lower.Contains("favourite")) && lower.Contains("remove") && CurrentCard != null)
        {
            if (_favorites.IsFavorite(CurrentCard.Id))
            {
                _favorites.Remove(CurrentCard.Id);
                AddAssistantMessage($"Removed \"{CurrentCard.Title}\" from local favorites.");
            }
            else
            {
                AddAssistantMessage($"\"{CurrentCard.Title}\" is not in your favorites.");
            }
            return true;
        }

        // Set folder
        if (lower.Contains("folder") && lower.Contains("move") && CurrentCard != null)
        {
            var idx = lower.IndexOf(" to ", StringComparison.Ordinal);
            if (idx >= 0)
            {
                var folder = input[(idx + 4)..].Trim().Trim('"', '\'');
                if (!string.IsNullOrEmpty(folder))
                {
                    if (!_favorites.IsFavorite(CurrentCard.Id))
                        _favorites.Add(CurrentCard.Artwork);
                    _favorites.SetFolder(CurrentCard.Id, folder);
                    AddAssistantMessage($"Moved \"{CurrentCard.Title}\" to folder \"{folder}\".");
                    return true;
                }
            }
        }

        // Show image by ID
        if (lower.Contains("show") && lower.Contains("image") && (lower.Contains("id") || lower.Contains("by id")))
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, @"(\d+)");
            if (match.Success)
            {
                AddAssistantMessage($"Fetching image by ID: {match.Value}…");
                try
                {
                    var card = await FetchArtworkByIdAsync(match.Value);
                    if (card != null)
                    {
                        // Set as current card and load image bytes
                        CurrentCard = card;
                        var bytes = await _imageLoader.FetchBytesAsync(card.ThumbnailUrl);
                        if (bytes != null)
                        {
                            if (CurrentSession != null)
                                SetSessionImage(bytes);
                            else
                                CurrentImageBytes = bytes;
                            
                            // Add message with image thumbnail
                            var msg = new AiChatMessage 
                            { 
                                Role = "assistant", 
                                Content = $"✓ Found: \"{card.Title}\" by {card.UserName}",
                                ImageBytes = bytes
                            };
                            Messages.Add(msg);
                        }
                        else
                        {
                            AddAssistantMessage($"✓ Found: \"{card.Title}\" by {card.UserName}");
                        }
                    }
                    else
                    {
                        AddAssistantMessage($"⚠ Could not find image with ID {match.Value}");
                    }
                }
                catch (Exception ex)
                {
                    AddSystemMessage($"✗ Failed to fetch image: {ex.Message}");
                }
                return true;
            }
        }

        // Show random/recent image from artist
        if ((lower.Contains("random") || lower.Contains("recent")) && lower.Contains("artist"))
        {
            var useRecent = lower.Contains("recent");
            // Extract artist identifier (ID or name)
            var match = System.Text.RegularExpressions.Regex.Match(input, @"artist\s+(.+?)(?:\s|$)");
            if (match.Success)
            {
                var artistIdentifier = match.Groups[1].Value.Trim().Trim('"', '\'');
                AddAssistantMessage($"Fetching {(useRecent ? "recent" : "random")} image from artist: {artistIdentifier}…");
                try
                {
                    var card = await FetchRandomByArtistAsync(artistIdentifier, useRecent);
                    if (card != null)
                    {
                        CurrentCard = card;
                        var bytes = await _imageLoader.FetchBytesAsync(card.ThumbnailUrl);
                        if (bytes != null)
                        {
                            if (CurrentSession != null)
                                SetSessionImage(bytes);
                            else
                                CurrentImageBytes = bytes;
                            
                            // Add message with image thumbnail
                            var msg = new AiChatMessage 
                            { 
                                Role = "assistant", 
                                Content = $"✓ Found: \"{card.Title}\" by {card.UserName}",
                                ImageBytes = bytes
                            };
                            Messages.Add(msg);
                        }
                        else
                        {
                            AddAssistantMessage($"✓ Found: \"{card.Title}\" by {card.UserName}");
                        }
                    }
                    else
                    {
                        AddAssistantMessage($"⚠ Could not find artworks for artist: {artistIdentifier}");
                    }
                }
                catch (Exception ex)
                {
                    AddSystemMessage($"✗ Failed to fetch artist artwork: {ex.Message}");
                }
                return true;
            }
        }

        // ── Pixiv API metadata queries ────────────────────────────────────────
        // Detect questions about the current artwork that the Pixiv API can answer
        // directly — no vision model needed.
        var isArtistQ   = lower.Contains("who is the artist") || lower.Contains("who made") || lower.Contains("who drew") || lower.Contains("who created") || lower.Contains("who is the creator") || lower.Contains("who is the author") || lower.Contains("who drew this") || lower.Contains("artist name") || lower.Contains("artist?");
        var isDateQ     = (lower.Contains("when") && (lower.Contains("upload") || lower.Contains("post") || lower.Contains("publish") || lower.Contains("release") || lower.Contains("made") || lower.Contains("create"))) || lower.Contains("upload date") || lower.Contains("posted date") || lower.Contains("release date");
        var isStatsQ    = (lower.Contains("how many") && (lower.Contains("view") || lower.Contains("like") || lower.Contains("bookmark"))) || lower.Contains("view count") || lower.Contains("like count") || lower.Contains("bookmark count") || (lower.Contains("how popular"));
        var isTagQ      = (lower.Contains("what") && lower.Contains("tag")) || lower.Contains("list the tag") || lower.Contains("show the tag") || lower.Contains("what are the tag");
        var isAboutQ    = lower.Contains("tell me about") || lower.StartsWith("info ") || lower == "info" || lower.StartsWith("about this") || lower.Contains("artwork info") || lower.Contains("artwork detail") || lower.Contains("pixiv info");
        // Resolve a card for the metadata query: use CurrentCard, or recover from session's stored artwork ID
        ArtworkCardViewModel? metaCardResolved = CurrentCard;
        if (metaCardResolved == null && CurrentSession?.PixivArtworkId is { Length: > 0 } sessionArtworkId)
            metaCardResolved = await FetchArtworkByIdAsync(sessionArtworkId);

        var isPixivMetaQ = (isArtistQ || isDateQ || isStatsQ || isTagQ || isAboutQ) && metaCardResolved != null;

        if (isPixivMetaQ && metaCardResolved is { } metaCard)
        {
            // Update CurrentCard so subsequent vision queries also have it
            if (CurrentCard == null) CurrentCard = metaCard;

            // Create the placeholder message up-front so PropertyChanged subscription fires correctly
            var placeholder = new AiChatMessage { Role = "assistant", Content = "⏳ Looking up artwork info from Pixiv…" };
            await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(placeholder));

            try
            {
                var body = await _pixiv.GetArtworkDetailAsync(metaCard.Id, ct);

                if (body == null)
                {
                    placeholder.Content = $"⚠ Could not load details for this artwork (ID: {metaCard.Id}). The artwork may be private or unavailable.";
                    return true;
                }
                else
                {
                    // Fetch artist profile for extra info
                    var artistUserId = body.UserId ?? metaCard.UserId;
                    PixivUserInfo? artistInfo = null;
                    if (!string.IsNullOrEmpty(artistUserId))
                        artistInfo = await _pixiv.GetArtistAsync(artistUserId, ct);

                    // Build response from what the user actually asked
                    var sb = new System.Text.StringBuilder();

                    if (isAboutQ || (isArtistQ && isDateQ) || (isArtistQ && isStatsQ))
                    {
                        // Full summary
                        sb.AppendLine($"**\"{body.IllustTitle ?? metaCard.Title}\"**");
                        sb.AppendLine($"🎨 **Artist:** {body.UserName ?? metaCard.UserName} (ID: {artistUserId}) — pixiv.net/users/{artistUserId}");
                        if (artistInfo?.Comment is { Length: > 0 } bio)
                            sb.AppendLine($"ℹ️ Bio: {bio}");
                        if (body.CreateDate is { Length: > 0 } cd)
                        {
                            if (DateTime.TryParse(cd, out var dt))
                                sb.AppendLine($"📅 **Uploaded:** {dt:MMMM d, yyyy} ({(DateTime.UtcNow - dt).Days} days ago)");
                            else
                                sb.AppendLine($"📅 **Uploaded:** {cd}");
                        }
                        sb.AppendLine($"👁 **Views:** {body.ViewCount:N0}   ❤️ **Likes:** {body.LikeCount:N0}   🔖 **Bookmarks:** {body.BookmarkCount:N0}");
                        if (body.XRestrict == 1) sb.AppendLine("🔞 Rated R-18");
                        if (body.AiType == 2)    sb.AppendLine("🤖 AI-generated");
                        if (body.PageCount > 1)  sb.AppendLine($"📄 {body.PageCount} pages");
                        var tags = body.Tags?.Tags.Select(t => t.Tag).Where(t => !string.IsNullOrEmpty(t)).ToList();
                        if (tags?.Count > 0)
                            sb.AppendLine($"🏷 **Tags:** {string.Join(", ", tags)}");
                        sb.AppendLine($"🔗 pixiv.net/artworks/{metaCard.Id}");
                    }
                    else if (isArtistQ)
                    {
                        sb.AppendLine($"🎨 **Artist:** {body.UserName ?? metaCard.UserName}");
                        sb.AppendLine($"🆔 User ID: {artistUserId}");
                        sb.AppendLine($"🔗 pixiv.net/users/{artistUserId}");
                        if (artistInfo?.Comment is { Length: > 0 } bio2)
                            sb.AppendLine($"ℹ️ Bio: {bio2}");
                        if (artistInfo?.IsFollowed == true)
                            sb.AppendLine("✅ You follow this artist.");
                    }
                    else if (isDateQ)
                    {
                        if (body.CreateDate is { Length: > 0 } cd2)
                        {
                            if (DateTime.TryParse(cd2, out var dt2))
                                sb.AppendLine($"📅 Uploaded on **{dt2:MMMM d, yyyy}** ({(DateTime.UtcNow - dt2).Days} days ago)");
                            else
                                sb.AppendLine($"📅 Upload date: {cd2}");
                        }
                        else
                            sb.AppendLine("📅 Upload date not available.");
                    }
                    else if (isStatsQ)
                    {
                        sb.AppendLine($"📊 **Stats for \"{body.IllustTitle ?? metaCard.Title}\"**");
                        sb.AppendLine($"👁 Views: {body.ViewCount:N0}");
                        sb.AppendLine($"❤️ Likes: {body.LikeCount:N0}");
                        sb.AppendLine($"🔖 Bookmarks: {body.BookmarkCount:N0}");
                        sb.AppendLine($"💬 Comments: {body.CommentCount:N0}");
                    }
                    else if (isTagQ)
                    {
                        var tags2 = body.Tags?.Tags.Select(t => t.Tag).Where(t => !string.IsNullOrEmpty(t)).ToList();
                        if (tags2?.Count > 0)
                            sb.AppendLine($"🏷 **Tags:** {string.Join(", ", tags2)}");
                        else
                            sb.AppendLine("🏷 No tags found.");
                    }

                    placeholder.Content = sb.ToString().TrimEnd();

                    return true;
                }
            }
            catch (OperationCanceledException) { return true; }
            catch (Exception ex)
            {
                placeholder.Content = $"⚠ API error: {ex.Message}";
                return true;
            }
        }

        // Show random artwork
        if (lower.Contains("random") && lower.Contains("artwork"))
        {
            AddAssistantMessage("Fetching a random artwork…");
            try
            {
                var card = await FetchRandomArtworkAsync();
                if (card != null)
                {
                    CurrentCard = card;
                    var bytes = await _imageLoader.FetchBytesAsync(card.ThumbnailUrl);
                    if (bytes != null)
                    {
                        if (CurrentSession != null)
                            SetSessionImage(bytes);
                        else
                            CurrentImageBytes = bytes;
                        
                        // Add message with image thumbnail
                        var msg = new AiChatMessage 
                        { 
                            Role = "assistant", 
                            Content = $"✓ Found: \"{card.Title}\" by {card.UserName}",
                            ImageBytes = bytes
                        };
                        Messages.Add(msg);
                    }
                    else
                    {
                        AddAssistantMessage($"✓ Found: \"{card.Title}\" by {card.UserName}");
                    }
                }
                else
                {
                    AddAssistantMessage("⚠ Could not fetch a random artwork");
                }
            }
            catch (Exception ex)
            {
                AddSystemMessage($"✗ Failed to fetch random artwork: {ex.Message}");
            }
            return true;
        }

        // ── Character / source identification ────────────────────────────────
        var isIdentifyQ = lower.Contains("who is") || lower.Contains("identify") ||
                          lower.Contains("what character") || lower.Contains("which character") ||
                          lower.Contains("character name") || lower.Contains("what series") ||
                          lower.Contains("source?") || lower.Contains("what anime") ||
                          lower.Contains("what game") || lower.Contains("what manga") ||
                          lower == "identify" || lower == "source" || lower == "characters";

        if (isIdentifyQ)
        {
            var pixivId = CurrentCard?.Id ?? CurrentSession?.PixivArtworkId;
            var hasImg  = CurrentImageBytes is { Length: > 0 };

            if (string.IsNullOrEmpty(pixivId) && !hasImg)
            {
                AddAssistantMessage("⚠ No image or artwork loaded. Open an artwork or attach an image first, then ask me to identify it.");
                return true;
            }

            var placeholder = new AiChatMessage { Role = "assistant", Content = "🔍 Looking up character and source info…" };
            await Dispatcher.UIThread.InvokeAsync(() => Messages.Add(placeholder));

            try
            {
                ImageLookupResult? result = null;

                // Prefer Pixiv ID lookup via Danbooru (fast, no image upload needed)
                if (!string.IsNullOrEmpty(pixivId))
                    result = await _imageLookup.LookupByPixivIdAsync(pixivId, ct);

                // Fall back to SauceNAO reverse image search if we have bytes
                if (result == null && hasImg)
                    result = await _imageLookup.LookupByImageBytesAsync(CurrentImageBytes!, ct: ct);

                var sb = new System.Text.StringBuilder();

                if (result == null)
                {
                    // External lookup failed — fall back to Pixiv metadata we already have
                    var card = CurrentCard;
                    if (card != null)
                    {
                        sb.AppendLine($"🔍 **Image Identification** *(via Pixiv metadata — not indexed on Danbooru/SauceNAO)*");
                        sb.AppendLine($"🎨 **Artist:** {card.UserName}");
                        sb.AppendLine($"📌 **Title:** {card.Title}");
                        if (card.Tags?.Count > 0)
                        {
                            var tagList = string.Join(", ", card.Tags.Take(20));
                            sb.AppendLine($"🏷 **Pixiv Tags:** {tagList}");
                        }
                        sb.AppendLine($"🔗 **Source:** https://www.pixiv.net/artworks/{card.Id}");
                        sb.AppendLine();
                        sb.AppendLine("ℹ️ Character names aren't available — this artwork isn't in Danbooru or SauceNAO's database. Try asking Hoshi to **describe** the image for a visual breakdown.");
                        placeholder.Content = sb.ToString().TrimEnd();
                    }
                    else
                    {
                        placeholder.Content = "⚠ Could not identify this image. It may not be indexed on Danbooru/SauceNAO yet.";
                    }
                    return true;
                }

                sb.AppendLine($"🔍 **Image Identification** *(via {result.Provider})*");

                if (!string.IsNullOrEmpty(result.CharacterTags))
                    sb.AppendLine($"👤 **Characters:** {result.CharacterTags}");
                else
                    sb.AppendLine("👤 **Characters:** Not identified");

                if (!string.IsNullOrEmpty(result.CopyrightTags))
                    sb.AppendLine($"📖 **Series/Copyright:** {result.CopyrightTags}");

                if (!string.IsNullOrEmpty(result.ArtistTags))
                    sb.AppendLine($"🎨 **Artist:** {result.ArtistTags}");

                if (!string.IsNullOrEmpty(result.GeneralTags))
                    sb.AppendLine($"🏷 **Tags:** {result.GeneralTags}");

                if (!string.IsNullOrEmpty(result.SourceUrl))
                    sb.AppendLine($"🔗 **Source:** {result.SourceUrl}");

                if (result.Similarity < 1.0)
                    sb.AppendLine($"📊 **Confidence:** {result.Similarity:F1}%");

                // Enrich with Pixiv tags if we didn't get character tags from external source
                if (string.IsNullOrEmpty(result.CharacterTags) && CurrentCard?.Tags?.Count > 0)
                {
                    var pixivTags = string.Join(", ", CurrentCard.Tags.Take(15));
                    sb.AppendLine($"🏷 **Pixiv Tags:** {pixivTags}");
                }

                placeholder.Content = sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                placeholder.Content = $"✗ Identification failed: {ex.Message}";
            }
            return true;
        }

        return false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private void SyncOllamaState()
    {
        IsEnabled    = _ollama.IsEnabled;
        StatusText   = _ollama.StatusText;
        IsModelReady = _ollama.IsReady;
        SendCommand.NotifyCanExecuteChanged();
    }

    private void AddUserMessage(string text)
        => Dispatcher.UIThread.Post(() => Messages.Add(new AiChatMessage { Role = "user", Content = text }));

    private void AddAssistantMessage(string text)
        => Dispatcher.UIThread.Post(() => Messages.Add(new AiChatMessage { Role = "assistant", Content = text }));

    private void AddSystemMessage(string text)
        => Dispatcher.UIThread.Post(() => Messages.Add(new AiChatMessage { Role = "system", Content = text }));
}

using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Pixora.Avalonia.Services;

/// <summary>
/// Desktop notification service for showing native OS notifications.
/// </summary>
public class NotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private bool _isInitialized;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows toast notifications are handled via Windows APIs
                _logger.LogInformation("Notification service initialized for Windows");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _logger.LogInformation("Notification service initialized for macOS");
            }
            else
            {
                _logger.LogInformation("Notification service initialized for Linux");
            }

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize notification service");
        }
    }

    /// <summary>
    /// Shows a desktop notification.
    /// </summary>
    public void ShowNotification(string title, string message, NotificationType type = NotificationType.Info)
    {
        if (!_isInitialized)
        {
            Initialize();
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ShowWindowsNotification(title, message, type);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ShowMacOSNotification(title, message, type);
            }
            else
            {
                ShowLinuxNotification(title, message, type);
            }

            _logger.LogDebug("Notification shown: {Title} - {Message}", title, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show notification");
        }
    }

    private void ShowWindowsNotification(string title, string message, NotificationType type)
    {
        try
        {
            // For Windows, we'll use a simple console debug message
            // In a production app, you'd use Windows.UI.Notifications or Microsoft.Toolkit.Uwp.Notifications
            _logger.LogInformation("[NOTIFICATION] {Title}: {Message}", title, message);

            // TODO: Implement proper Windows toast notifications using Windows SDK
            // This would require adding a package reference to Microsoft.Toolkit.Uwp.Notifications
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show Windows notification");
        }
    }

    private void ShowMacOSNotification(string title, string message, NotificationType type)
    {
        try
        {
            // Use osascript to show native macOS notifications
            var script = $@"display notification ""{message.Replace("\"", "\\\"")}"" with title ""{title.Replace("\"", "\\\"")}""";
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"-e '{script}'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show macOS notification");
        }
    }

    private void ShowLinuxNotification(string title, string message, NotificationType type)
    {
        try
        {
            // Use notify-send for Linux desktop notifications
            var urgency = type switch
            {
                NotificationType.Error => "critical",
                NotificationType.Warning => "normal",
                _ => "low"
            };

            var icon = type switch
            {
                NotificationType.Success => "dialog-information",
                NotificationType.Warning => "dialog-warning",
                NotificationType.Error => "dialog-error",
                _ => "dialog-information"
            };

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notify-send",
                    Arguments = $"--urgency={urgency} --icon={icon} \"{title}\" \"{message}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show Linux notification");
        }
    }

    /// <summary>
    /// Shows a notification for new artist submissions with artwork ID for click handling.
    /// </summary>
    public void ShowNewSubmissionNotification(string artistName, int count, string? firstArtworkTitle, string? artworkId = null)
    {
        var title = $"🎨 {artistName} posted {count} new artwork{(count > 1 ? "s" : "")}";
        var message = firstArtworkTitle ?? "Click to view";
        var url = artworkId != null ? $"https://www.pixiv.net/en/artworks/{artworkId}" : null;

        // Store for click handling
        _lastNotification = new NotificationClickedEventArgs(title, message, artworkId, url, NotificationType.Info);

        ShowNotification(title, message, NotificationType.Info);
    }

    /// <summary>
    /// Shows a notification when a download job completes.
    /// </summary>
    public void ShowJobCompletedNotification(string jobName, int succeeded, int failed, string? firstArtworkId = null)
    {
        var title = failed > 0 ? "❌ Download Job Completed with Errors" : "✅ Download Job Completed";
        var message = $"{jobName}\n{succeeded} succeeded, {failed} failed";
        var type = failed > 0 ? NotificationType.Warning : NotificationType.Success;
        var url = firstArtworkId != null ? $"https://www.pixiv.net/en/artworks/{firstArtworkId}" : null;

        _lastNotification = new NotificationClickedEventArgs(title, message, firstArtworkId, url, type);

        ShowNotification(title, message, type);
    }

    private NotificationClickedEventArgs? _lastNotification;

    /// <summary>
    /// Gets the last notification that was shown (for click handling).
    /// </summary>
    public NotificationClickedEventArgs? GetLastNotification() => _lastNotification;

    /// <summary>
    /// Event raised when a notification is clicked.
    /// </summary>
    public event EventHandler<NotificationClickedEventArgs>? NotificationClicked;
}

/// <summary>
/// Event args for notification click events.
/// </summary>
public class NotificationClickedEventArgs : EventArgs
{
    public string Title { get; }
    public string Message { get; }
    public string? ArtworkId { get; }
    public string? Url { get; }
    public NotificationType Type { get; }

    public NotificationClickedEventArgs(string title, string message, string? artworkId = null, string? url = null, NotificationType type = NotificationType.Info)
    {
        Title = title;
        Message = message;
        ArtworkId = artworkId;
        Url = url;
        Type = type;
    }
}

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

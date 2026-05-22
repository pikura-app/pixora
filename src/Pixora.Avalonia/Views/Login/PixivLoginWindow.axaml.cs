using Avalonia.Controls;
using Avalonia.Controls.Linux;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Pixora.Avalonia.Services;
using Pixora.Core.Services;
using Pixora.Core.Settings;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pixora.Avalonia.Views.Login;

public partial class PixivLoginWindow : Window
{
    private bool _completed;
    private bool _navigationStarted;
    private readonly bool _clearCookies;

    public bool LoginSucceeded { get; private set; }

    /// <summary>
    /// True when the WebView backend (WPE / WebKitGTK) could not be initialised.
    /// The caller should fall back to <see cref="ManualCookieDialog"/> in this case.
    /// </summary>
    public bool WebViewFailed { get; private set; }

    /// <summary>
    /// Checks whether the required Linux WebView native libraries are present.
    /// Returns null on non-Linux; returns a missing-library description string on Linux if absent.
    /// </summary>
    public static string? CheckLinuxWebViewLibs()
    {
        if (!OperatingSystem.IsLinux()) return null;
        // WPE WebKit is the Avalonia default Linux backend (offscreen SHM, no GPU needed).
        // All three libs must be present for NativeWebView to render.
        foreach (var lib in new[] { "libwpewebkit-2.0.so.0", "libwpe-1.0.so.1", "libWPEBackend-fdo-1.0.so.1" })
        {
            if (NativeLibraryExists(lib)) return null;  // at least one backend found — let Avalonia try
        }
        return "libwpewebkit-2.0-3 libwpe-1.0-1 libwpebackend-fdo-1.0-1";
    }

    private static bool NativeLibraryExists(string lib)
    {
        try
        {
            var handle = System.Runtime.InteropServices.NativeLibrary.Load(lib);
            if (handle != IntPtr.Zero) { System.Runtime.InteropServices.NativeLibrary.Free(handle); return true; }
        }
        catch { }
        return false;
    }

    public PixivLoginWindow() : this(false) { }

    public PixivLoginWindow(bool clearCookiesForNewAccount)
    {
        _clearCookies = clearCookiesForNewAccount;

        if (_clearCookies)
        {
            // Delete the WebView2 cookie database from disk BEFORE InitializeComponent()
            // so the WebView process starts with a clean profile. This avoids touching
            // Pixiv's server (no /logout.php) so all other saved accounts stay valid.
            TryDeleteWebView2CookiesFromDisk();
        }

        InitializeComponent();

        // On Linux, detect missing native WebView libraries before trying to use the control.
        // An absent WPE/WebKitGTK library will cause the WebView to stay blank with no error.
        if (OperatingSystem.IsLinux() && CheckLinuxWebViewLibs() is string missingLib)
        {
            WebViewFailed = true;
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = $"WebView unavailable: install {missingLib} and restart.";
                WebView.IsVisible = false;
            });
            return;
        }

        // Safety net: if the WebView never fires NavigationStarted within 8 seconds the
        // backend silently failed (e.g. libGL missing, Wayland compositor issue).
        // Mark WebViewFailed so the caller knows to offer the manual fallback.
        _ = Task.Run(async () =>
        {
            await Task.Delay(8000);
            if (!_navigationStarted && !_completed)
            {
                WebViewFailed = true;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText.Text = "WebView did not load. Use \"Enter cookie manually\" instead.";
                });
            }
        });

        WebView.EnvironmentRequested += (_, args) =>
        {
            // Windows WebView2: enable InPrivate mode so no cookies or session data
            // are ever written to disk. Each login window starts completely fresh.
            // We set the property via reflection because the concrete EventArgs type
            // (WindowsWebView2EnvironmentRequestedEventArgs) lives in a namespace that
            // varies across Avalonia.Controls.WebView versions.
            if (_clearCookies && OperatingSystem.IsWindows())
            {
                var t = args.GetType();
                // Set InPrivate mode so WebView2 uses an ephemeral (in-memory) profile.
                t.GetProperty("IsInPrivateModeEnabled")?.SetValue(args, true);
                // Also point UserDataFolder at a unique temp dir so this instance never
                // shares a profile with the main app WebView or a previous login window.
                var tmpDir = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), $"pixora_login_{Guid.NewGuid():N}");
                t.GetProperty("UserDataFolder")?.SetValue(args, tmpDir);
            }

            // WPE WebKit (default) uses offscreen SHM rendering — no GPU required, works in VMs.
            // Do NOT set PreferWebKitGtkInstead; WebKitGTK needs a compositor and fails without GPU.
        };

        if (_clearCookies)
        {
            // Belt-and-suspenders in-proc clear for non-WebView2 platforms
            // (macOS WKWebView, Linux WebKitGTK) that don't support InPrivate.
            WebView.NavigationCompleted -= WebView_NavigationCompleted;
            WebView.Source = new Uri("about:blank");
            WebView.NavigationCompleted += ClearCookiesThenLogin;
        }
    }

    /// <summary>
    /// Deletes the WebView2 cookies file(s) from disk so the next WebView instance
    /// starts with a fresh, unauthenticated profile. Safe to call before InitializeComponent.
    /// </summary>
    private static void TryDeleteWebView2CookiesFromDisk()
    {
        try
        {
            // WebView2 stores its persistent data under %LocalAppData%\<ExeName>.WebView2
            // or %AppData%\<ExeName>\EBWebView.  We target the most common locations.
            var exeName = System.Diagnostics.Process.GetCurrentProcess().ProcessName; // "Pixora"
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var candidates = new[]
            {
                // WebView2 fixed-version / default locations
                System.IO.Path.Combine(localApp, $"{exeName}.WebView2", "EBWebView", "Default", "Cookies"),
                System.IO.Path.Combine(localApp, $"{exeName}.WebView2", "EBWebView", "Default", "Network", "Cookies"),
                System.IO.Path.Combine(localApp, $"{exeName}.WebView2", "Default", "Cookies"),
                System.IO.Path.Combine(localApp, $"{exeName}.WebView2", "Default", "Network", "Cookies"),
                // Avalonia.Controls.WebView may also use %AppData%\Pixora\EBWebView
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Pixora", "EBWebView", "Default", "Cookies"),
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Pixora", "EBWebView", "Default", "Network", "Cookies"),
            };

            foreach (var path in candidates)
            {
                try
                {
                    if (System.IO.File.Exists(path))
                        System.IO.File.Delete(path);
                }
                catch { /* locked by another process — will fall back to in-proc API */ }
            }
        }
        catch { /* non-fatal */ }
    }

    private async void ClearCookiesThenLogin(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (sender is not NativeWebView wv) return;
        var uri = wv.Source?.ToString() ?? string.Empty;
        if (uri != "about:blank" && !string.IsNullOrEmpty(uri)) return;

        WebView.NavigationCompleted -= ClearCookiesThenLogin;

        // In-proc cookie API clear (belt-and-suspenders on top of the disk deletion).
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var cm = wv.TryGetCookieManager();
                if (cm == null) return;
                try
                {
                    var mi = cm.GetType().GetMethod("DeleteAllCookies");
                    mi?.Invoke(cm, null);
                }
                catch { /* non-fatal */ }
                try
                {
                    var cookies = await cm.GetCookiesAsync();
                    var del = cm.GetType().GetMethod("DeleteCookie");
                    if (del != null)
                        foreach (var c in cookies)
                            try { del.Invoke(cm, new object?[] { c }); } catch { }
                }
                catch { /* non-fatal */ }
            });
            await Task.Delay(200);
        }
        catch { /* non-fatal */ }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            WebView.NavigationCompleted += WebView_NavigationCompleted;
            wv.Source = new Uri("https://accounts.pixiv.net/login?lang=en&source=pc&view_type=page");
        });
    }

    // NavigationCompleted may fire on any thread depending on the WebView2 host.
    // Always dispatch the entire handler to the UI thread so webView (COM/WinRT thread-affined)
    // is only accessed from the thread that owns it.
    private void WebView_NavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _navigationStarted = true;
        if (_completed || sender is not NativeWebView webView) return;
        if (!e.IsSuccess) return;

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var uri = webView.Source;
            if (uri is null) return;
            if (!uri.Host.Equals("www.pixiv.net", StringComparison.OrdinalIgnoreCase)) return;
            await HandleLoginAsync(webView);
        });
    }

    // Runs entirely on the UI thread. Task.Delay resumes on threadpool but webView is only
    // touched before / after those delays via InvokeAsync wrappers to re-enter the UI thread.
    private async Task HandleLoginAsync(NativeWebView webView)
    {
        StatusText.Text = "Detecting session…";

        try
        {
            // Small delay to let the page settle — off UI thread is fine here
            await Task.Delay(1500);

            const string script = """
                (function() {
                    try {
                        var xhr = new XMLHttpRequest();
                        xhr.open('GET', 'https://www.pixiv.net/touch/ajax/user/self/status?lang=en', false);
                        xhr.withCredentials = true;
                        xhr.send();
                        var j = JSON.parse(xhr.responseText);
                        var u = j && j.body && j.body.user_status;
                        if (u && u.is_logged_in) {
                            return JSON.stringify({ ok: true, userId: String(u.user_id), userName: u.user_name });
                        }
                        return JSON.stringify({ ok: false, raw: xhr.responseText.substring(0, 300) });
                    } catch(e) {
                        return JSON.stringify({ ok: false, err: String(e) });
                    }
                })()
                """;

            // Back on UI thread to call InvokeScript (webView is thread-affined)
            var result = await Dispatcher.UIThread.InvokeAsync(() => webView.InvokeScript(script));

            if (string.IsNullOrWhiteSpace(result))
            {
                await Dispatcher.UIThread.InvokeAsync(() => StatusText.Text = "Retrying session check…");
                await Task.Delay(2000);
                result = await Dispatcher.UIThread.InvokeAsync(() => webView.InvokeScript(script));
                if (string.IsNullOrWhiteSpace(result))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusText.Text = "Could not detect session. Try signing in again.";
                        _completed = false;
                    });
                    return;
                }
            }

            var json = result;
            if (json.StartsWith("\"") && json.EndsWith("\""))
                json = JsonSerializer.Deserialize<string>(json) ?? json;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
            {
                var rawMsg = root.TryGetProperty("raw", out var rp) ? rp.GetString() :
                             root.TryGetProperty("err", out var ep) ? ep.GetString() : json;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText.Text = $"Not signed in: {rawMsg?[..Math.Min(rawMsg?.Length ?? 0, 120)]}";
                    _completed = false;
                });
                return;
            }

            var userId   = root.TryGetProperty("userId",   out var uidProp) ? uidProp.GetString() : null;
            var userName = root.TryGetProperty("userName", out var unProp)  ? unProp.GetString()  : null;

            if (string.IsNullOrWhiteSpace(userId))
            {
                await Dispatcher.UIThread.InvokeAsync(() => StatusText.Text = "Session detected but user ID missing — try again.");
                return;
            }

            _completed = true;
            await Dispatcher.UIThread.InvokeAsync(() => StatusText.Text = "Confirming session…");

            // Extract PHPSESSID — GetCookiesAsync must be awaited on the UI thread
            string? sid = null;
            try
            {
                sid = await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var cm = webView.TryGetCookieManager();
                    if (cm == null) return null;
                    var cookies = await cm.GetCookiesAsync();
                    return cookies.FirstOrDefault(c =>
                        string.Equals(c.Name, "PHPSESSID", StringComparison.OrdinalIgnoreCase))?.Value;
                });
            }
            catch { /* non-fatal */ }

            var settings = AppServices.Get<SettingsService>();
            settings.Update(s =>
            {
                if (!string.IsNullOrWhiteSpace(sid)) s.PhpSessId = sid;
                s.UserId   = userId;
                s.UserName = userName ?? userId;
            });

            // Register the profile NOW so it appears in the accounts list even if
            // the network validation below fails for any reason.
            try { AppServices.Get<AccountService>().UpsertFromCurrentSession(); }
            catch { /* non-fatal */ }

            // Network validation — entirely off UI thread, no webView access
            try
            {
                await Task.Run(async () =>
                {
                    var client = AppServices.Get<PixivClient>();
                    await client.ValidateSessionAsync();
                });
            }
            catch { /* non-fatal — the profile is already saved */ }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var displayName = settings.Current.UserName ?? settings.Current.UserId;
                LoginSucceeded  = true;
                StatusText.Text = $"Signed in as {displayName}";
            });
            await Task.Delay(800);
            await Dispatcher.UIThread.InvokeAsync(() => Close());
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText.Text = $"Error: {ex.Message}";
                _completed = false;
            });
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}

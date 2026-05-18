using Avalonia.Controls;
using Avalonia.Controls.Linux;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Pixora.Avalonia.Services;
using Pixora.Core.Services;
using Pixora.Core.Settings;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pixora.Avalonia.Views.Login;

public partial class PixivLoginWindow : Window
{
    private bool _completed;

    public bool LoginSucceeded { get; private set; }

    public PixivLoginWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsLinux())
        {
            WebView.EnvironmentRequested += (_, args) =>
            {
                if (args is LinuxWpeWebViewEnvironmentRequestedEventArgs wpe)
                    wpe.PreferWebKitGtkInstead = true;
            };
        }
    }

    private async void WebView_NavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (_completed || sender is not NativeWebView webView) return;
        if (!e.IsSuccess) return;

        var uri = webView.Source;
        if (uri is null) return;

        // Only act once we're on pixiv.net (post-login redirect)
        if (!uri.Host.Equals("www.pixiv.net", StringComparison.OrdinalIgnoreCase)) return;

        StatusText.Text = "Detecting session…";

        try
        {
            // Give the page a moment to fully establish session cookies after redirect
            await Task.Delay(1500);

            // Use synchronous XHR — InvokeScript may not await async/Promise returns.
            // We call Pixiv's status endpoint synchronously from within the WebView
            // so the browser sends all cookies (including HttpOnly) automatically.
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

            var result = await webView.InvokeScript(script);

            if (string.IsNullOrWhiteSpace(result))
            {
                StatusText.Text = "Retrying session check…";
                await Task.Delay(2000);
                result = await webView.InvokeScript(script);
                if (string.IsNullOrWhiteSpace(result))
                {
                    StatusText.Text = "Could not detect session. Try signing in again.";
                    _completed = false;
                    return;
                }
            }

            // InvokeScript returns the JS value serialized as a JSON string.
            // If the script returns a string (JSON.stringify result), it comes back
            // double-encoded: "\"{ ... }\"" — unwrap the outer string layer.
            var json = result;
            if (json.StartsWith("\"") && json.EndsWith("\""))
                json = JsonSerializer.Deserialize<string>(json) ?? json;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
            {
                var rawMsg = root.TryGetProperty("raw", out var rp) ? rp.GetString() :
                             root.TryGetProperty("err", out var ep) ? ep.GetString() : json;
                StatusText.Text = $"Not signed in: {rawMsg?[..Math.Min(rawMsg?.Length ?? 0, 120)]}";
                _completed = false;
                return;
            }

            var userId = root.TryGetProperty("userId", out var uidProp) ? uidProp.GetString() : null;
            var userName = root.TryGetProperty("userName", out var unProp) ? unProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(userId))
            {
                StatusText.Text = "Session detected but user ID missing — try again.";
                return;
            }

            _completed = true;
            StatusText.Text = "Session confirmed — extracting cookie…";

            // Now that we know we're logged in, get the PHPSESSID via WebView2 cookie manager
            var sid = await TryGetPhpSessIdViaCookieManagerAsync(webView);

            var settings = AppServices.Get<SettingsService>();

            if (!string.IsNullOrWhiteSpace(sid))
            {
                settings.Update(s =>
                {
                    s.PhpSessId = sid;
                    s.UserId = userId;
                    s.UserName = userName ?? userId;
                });
            }
            else
            {
                // Cookie manager unavailable — store user info only and let validate fill the rest
                // The session is confirmed active so save what we can; user will need to paste PHPSESSID once
                settings.Update(s =>
                {
                    s.UserId = userId;
                    s.UserName = userName ?? userId;
                });
            }

            // Run full validation to fill in any remaining fields
            var client = AppServices.Get<PixivClient>();
            await client.ValidateSessionAsync();

            AppServices.Get<AccountService>().UpsertFromCurrentSession();

            LoginSucceeded = true;
            StatusText.Text = $"Signed in as {settings.Current.UserName ?? settings.Current.UserId}";
            await Task.Delay(800);
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            _completed = false;
        }
    }

    /// <summary>
    /// Reads PHPSESSID from the WebView cookie store.
    /// Uses the official cross-platform TryGetCookieManager() API first (works on Windows and macOS).
    /// Falls back to JS document.cookie, then manual prompt as a last resort.
    /// </summary>
    private async Task<string?> TryGetPhpSessIdViaCookieManagerAsync(NativeWebView webView)
    {
        // 1. Try the official Avalonia cross-platform cookie manager (Windows + macOS)
        try
        {
            var cookieManager = webView.TryGetCookieManager();
            if (cookieManager != null)
            {
                var cookies = await cookieManager.GetCookiesAsync();
                var sid = cookies
                    .FirstOrDefault(c => string.Equals(c.Name, "PHPSESSID", StringComparison.OrdinalIgnoreCase));
                if (sid != null && !string.IsNullOrWhiteSpace(sid.Value))
                    return sid.Value;
            }
        }
        catch { /* non-fatal — fall through */ }

        // 2. Try JS document.cookie (only works if not HttpOnly, but worth trying)
        try
        {
            const string script = """
                (function() {
                    var m = document.cookie.match(/(?:^|;\s*)PHPSESSID=([^;]+)/);
                    return m ? m[1] : '';
                })()
                """;
            var result = await webView.InvokeScript(script);
            if (!string.IsNullOrWhiteSpace(result) && result != "\"\"" && result != "null")
            {
                var value = result.Trim('"');
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        catch { /* non-fatal */ }

        // 3. Last resort: ask the user to paste it manually
        StatusText.Text = "Session confirmed — paste your PHPSESSID below to complete sign-in.";
        return await PromptForPhpSessIdAsync();
    }

    private async Task<string?> PromptForPhpSessIdAsync()
    {
        var dialog = new ManualCookieDialog();
        var owner  = TopLevel.GetTopLevel(this) as Window ?? this;
        await dialog.ShowDialog(owner);
        return string.IsNullOrWhiteSpace(dialog.PhpSessId) ? null : dialog.PhpSessId;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}

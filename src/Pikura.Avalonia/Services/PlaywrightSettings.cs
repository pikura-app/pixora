using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Pikura.Avalonia.Services;

/// <summary>
/// Centralises everything Playwright-related so the rest of the app doesn't have
/// to know about cache paths, browser revisions, or environment variables.
///
/// Two responsibilities:
/// <list type="bullet">
///   <item><b>Cache isolation</b> — we point <c>PLAYWRIGHT_BROWSERS_PATH</c> at a
///     Pikura-owned folder so we don't share a Chromium install with whatever
///     other .NET app on the machine also happens to use Playwright. Cross-app
///     installs would otherwise corrupt each other's state when one upgrades.</item>
///   <item><b>Revision pinning</b> — Playwright's <c>install</c> command always
///     pulls the revision in <c>browsers.json</c> shipped with the NuGet. When
///     we upgrade the NuGet (e.g. 1.49 → 1.50) that revision bumps and Playwright
///     would silently re-download Chromium the next time we asked it to install,
///     even though the user already has a perfectly working binary. By recording
///     the revision we actually installed and refusing to re-trigger install
///     while that folder exists, we make the cache durable across NuGet bumps.</item>
/// </list>
///
/// Call <see cref="EnsureBrowsersPathIsolation"/> from <c>Program.Main</c>
/// BEFORE Avalonia starts so the env var is in place for every Playwright call.
/// </summary>
public static class PlaywrightSettings
{
    /// <summary>
    /// Cache directory we want Playwright to use. Created on demand by the
    /// install step; never deleted by us. Placed under LocalAppData so it
    /// survives across version updates of Pikura itself.
    /// </summary>
    public static string BrowsersCacheDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pikura", "playwright-browsers");

    /// <summary>
    /// Sets <c>PLAYWRIGHT_BROWSERS_PATH</c> to <see cref="BrowsersCacheDir"/>
    /// for the current process. Safe to call multiple times; only takes effect
    /// if the variable isn't already explicitly set by the user (so power users
    /// who point Playwright at a shared cache can still override us).
    /// </summary>
    public static void EnsureBrowsersPathIsolation()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH")))
            return;

        try
        {
            Directory.CreateDirectory(BrowsersCacheDir);
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", BrowsersCacheDir);
        }
        catch
        {
            // If we can't write the cache dir for any reason (permissions,
            // read-only filesystem) just let Playwright fall back to its default
            // ~/.cache/ms-playwright location. Login still works there, we just
            // give up the isolation guarantees.
        }
    }

    /// <summary>
    /// Returns the Chromium revision currently extracted on disk, or null if
    /// none is installed. Reads it from the folder name (e.g. "chromium-1148").
    /// </summary>
    public static string? GetInstalledChromiumRevision()
    {
        try
        {
            if (!Directory.Exists(BrowsersCacheDir)) return null;
            var chromiumDir = Directory.EnumerateDirectories(BrowsersCacheDir, "chromium-*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (chromiumDir is null) return null;
            var name = Path.GetFileName(chromiumDir);
            // "chromium-1148" → "1148"
            var dash = name.IndexOf('-');
            return dash >= 0 && dash < name.Length - 1 ? name[(dash + 1)..] : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the Chromium revision Playwright would install if asked right now,
    /// pulled from the <c>browsers.json</c> shipped inside the NuGet's
    /// <c>.playwright</c> folder under the app's output directory. Null when the
    /// file can't be found or parsed (we then fall back to bare "any revision" checks).
    /// </summary>
    public static string? GetBundledChromiumRevision()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, ".playwright", "package", "browsers.json");
            if (!File.Exists(path)) return null;

            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("browsers", out var browsers)) return null;

            foreach (var b in browsers.EnumerateArray())
            {
                if (b.TryGetProperty("name", out var n) && n.GetString() == "chromium"
                    && b.TryGetProperty("revision", out var r))
                {
                    return r.ValueKind switch
                    {
                        JsonValueKind.String => r.GetString(),
                        JsonValueKind.Number => r.GetInt64().ToString(),
                        _ => null,
                    };
                }
            }
        }
        catch
        {
            // Best-effort metadata read; failure is non-fatal.
        }
        return null;
    }

    /// <summary>
    /// True when Chromium needs to be installed. Considers ANY chromium-* folder
    /// in the cache acceptable — this is the heart of the revision pin: even if
    /// the NuGet expects revision 1200 but we already have 1148 on disk, we
    /// continue using 1148 instead of triggering a 150 MB re-download. Playwright
    /// itself is fine with this: <see cref="Microsoft.Playwright.IBrowserType.LaunchAsync"/>
    /// against an older-but-compatible Chromium works because the wire protocol
    /// is backward compatible within minor SDK versions.
    /// </summary>
    public static bool NeedsChromiumInstall() => GetInstalledChromiumRevision() is null;
}

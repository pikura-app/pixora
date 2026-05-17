using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pixora.Core.Services;

/// <summary>
/// Headless browser service that automatically solves Cloudflare challenges
/// and extracts the cf_clearance cookie without showing any UI.
/// </summary>
public sealed class CloudflareSessionService
{
    private readonly ILogger<CloudflareSessionService> _logger;
    private readonly string _phpSessId;

    public CloudflareSessionService(string phpSessId, ILogger<CloudflareSessionService>? logger = null)
    {
        _phpSessId = phpSessId;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CloudflareSessionService>.Instance;
    }

    /// <summary>
    /// Refreshes the Cloudflare session by launching a headless browser,
    /// navigating to Pixiv, waiting for Cloudflare verification to complete,
    /// and extracting the cf_clearance cookie.
    /// </summary>
    public async Task<string?> RefreshSessionAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting headless browser for Cloudflare session refresh...");
        
        try
        {
            // Install Playwright browsers if needed (first run)
            Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        }
        catch { /* ignore - may already be installed */ }

        IPlaywright? playwright = null;
        IBrowser? browser = null;

        try
        {
            playwright = await Playwright.CreateAsync();
            
            // Launch headless Chromium with stealth args to appear more like a real browser
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--disable-blink-features=AutomationControlled",
                    "--disable-web-security",
                    "--disable-features=IsolateOrigins,site-per-process",
                }
            });

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    ["Accept-Language"] = "en-US,en;q=0.9",
                    ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8",
                }
            });

            // Add PHPSESSID cookie if available
            if (!string.IsNullOrEmpty(_phpSessId))
            {
                await context.AddCookiesAsync(new[]
                {
                    new Cookie
                    {
                        Name = "PHPSESSID",
                        Value = _phpSessId,
                        Domain = ".pixiv.net",
                        Path = "/",
                        HttpOnly = true,
                        Secure = true,
                    }
                });
            }

            var page = await context.NewPageAsync();
            
            _logger.LogInformation("Navigating to pixiv.net...");
            
            // Navigate to Pixiv - this will trigger Cloudflare challenge if needed
            var response = await page.GotoAsync("https://www.pixiv.net", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 60000,
            });

            if (response == null)
            {
                _logger.LogWarning("Failed to navigate to Pixiv");
                return null;
            }

            _logger.LogInformation("Page loaded, checking for Cloudflare challenge...");

            // Wait a bit for any Cloudflare challenge to complete
            // The challenge usually redirects to the main page when done
            await Task.Delay(3000, ct);

            // Wait for the page to be fully loaded (not the challenge page)
            var maxAttempts = 30; // 30 seconds max
            for (int i = 0; i < maxAttempts; i++)
            {
                ct.ThrowIfCancellationRequested();
                
                var title = await page.TitleAsync();
                var url = page.Url;
                
                _logger.LogDebug("Attempt {Attempt}: Title='{Title}', URL='{Url}'", i + 1, title, url);

                // If we're on pixiv.net (not a challenge page), we're good
                if (url.Contains("pixiv.net") && !title.Contains("Just a moment") && !title.Contains("Checking"))
                {
                    _logger.LogInformation("Cloudflare challenge completed successfully");
                    break;
                }

                // Still on challenge page, wait more
                await Task.Delay(1000, ct);
            }

            // Get all cookies and find cf_clearance
            var cookies = await context.CookiesAsync(new[] { "https://www.pixiv.net" });
            var cfClearance = cookies.FirstOrDefault(c => c.Name == "cf_clearance")?.Value;

            if (!string.IsNullOrEmpty(cfClearance))
            {
                _logger.LogInformation("Successfully captured cf_clearance cookie");
                return cfClearance;
            }
            else
            {
                _logger.LogWarning("cf_clearance cookie not found - may not be needed or challenge failed");
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Session refresh cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh Cloudflare session");
            return null;
        }
        finally
        {
            if (browser != null)
                await browser.CloseAsync();
            playwright?.Dispose();
        }
    }
}

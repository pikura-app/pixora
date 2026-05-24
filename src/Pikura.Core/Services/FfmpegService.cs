using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Pikura.Core.Settings;

namespace Pikura.Core.Services;

/// <summary>
/// Detects and (optionally) installs an ffmpeg binary that the rest of the app
/// can use for ugoira conversion. The binary is resolved in this priority order:
///   1. <see cref="AppSettings.FfmpegPath"/> if it exists
///   2. <c>ffmpeg</c> on PATH
///   3. App-managed install under <c>%LOCALAPPDATA%/Pikura/tools/ffmpeg/</c>
/// </summary>
public sealed class FfmpegService
{
    private readonly SettingsService _settings;
    private readonly ILogger<FfmpegService> _logger;

    public FfmpegService(SettingsService settings, ILogger<FfmpegService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Folder under app data where the auto-installed binary lives.</summary>
    public static string AppManagedDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Pikura", "tools", "ffmpeg");

    /// <summary>Reports the resolved ffmpeg executable path, or null when unavailable.</summary>
    public string? GetExecutablePath()
    {
        var configured = _settings.Current.FfmpegPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var onPath = ResolveFromPath();
        if (onPath != null) return onPath;

        var managed = ResolveFromManagedDir();
        if (managed != null) return managed;

        return null;
    }

    /// <summary>Returns true when ffmpeg is reachable somewhere on the system.</summary>
    public bool IsAvailable() => GetExecutablePath() != null;

    /// <summary>
    /// Probes <c>ffmpeg -version</c> on the resolved binary and returns the version
    /// string, or null when the binary fails or isn't found.
    /// </summary>
    public async Task<string?> ProbeVersionAsync(CancellationToken ct = default)
    {
        var exe = GetExecutablePath();
        if (exe == null) return null;
        try
        {
            var psi = new ProcessStartInfo(exe, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var stdout = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            // First line looks like:  ffmpeg version n6.1 Copyright (c) ...
            var line = stdout.Split('\n', 2)[0].Trim();
            var m = Regex.Match(line, @"^ffmpeg version (\S+)");
            return m.Success ? m.Groups[1].Value : line;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffmpeg -version probe failed for {Exe}", exe);
            return null;
        }
    }

    /// <summary>
    /// Downloads a static ffmpeg build into <see cref="AppManagedDir"/> for the
    /// current OS+arch, sets <see cref="AppSettings.FfmpegPath"/>, and returns
    /// the new path. Throws on failure.
    /// </summary>
    public async Task<string> InstallAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        var (url, isZip, exeRelativePath) = ResolveDownloadInfo();
        Directory.CreateDirectory(AppManagedDir);

        status?.Report($"Downloading ffmpeg from {Host(url)}…");
        _logger.LogInformation("Downloading ffmpeg from {Url}", url);

        var archivePath = Path.Combine(AppManagedDir, isZip ? "ffmpeg-download.zip" : "ffmpeg-download.tar.xz");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        await using (var src = await http.GetStreamAsync(url, ct).ConfigureAwait(false))
        await using (var dst = File.Create(archivePath))
        {
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
        }

        status?.Report("Extracting ffmpeg…");
        var stagingDir = Path.Combine(AppManagedDir, "_staging");
        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
        Directory.CreateDirectory(stagingDir);

        if (isZip)
        {
            ZipFile.ExtractToDirectory(archivePath, stagingDir, overwriteFiles: true);
        }
        else
        {
            // tar.xz — fall back to system tar (every modern Linux/macOS ships it).
            var psi = new ProcessStartInfo("tar", $"-xJf \"{archivePath}\" -C \"{stagingDir}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to launch tar to extract ffmpeg archive.");
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"tar exited with code {p.ExitCode} extracting {archivePath}.");
        }

        // Find the extracted ffmpeg executable. Builds are typically nested in a
        // single top-level directory like ffmpeg-6.1-essentials_build/bin/ffmpeg.exe.
        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string? foundExe = null;
        foreach (var candidate in Directory.EnumerateFiles(stagingDir, exeName, SearchOption.AllDirectories))
        {
            foundExe = candidate;
            break;
        }
        if (foundExe == null)
            throw new InvalidOperationException($"Could not find {exeName} inside the downloaded archive.");

        // Move the entire enclosing folder up to the managed dir so DLLs (Windows) come too.
        var enclosingDir = Path.GetDirectoryName(foundExe)!;
        var finalBinDir = Path.Combine(AppManagedDir, "bin");
        if (Directory.Exists(finalBinDir)) Directory.Delete(finalBinDir, recursive: true);
        Directory.Move(enclosingDir, finalBinDir);

        var finalExe = Path.Combine(finalBinDir, exeName);
        if (!OperatingSystem.IsWindows())
        {
            // Mark executable on Unix.
            try
            {
                var chmod = Process.Start(new ProcessStartInfo("chmod", $"+x \"{finalExe}\"") { UseShellExecute = false });
                if (chmod != null) await chmod.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch { /* best-effort */ }
        }

        // Cleanup
        try { File.Delete(archivePath); } catch { }
        try { Directory.Delete(stagingDir, recursive: true); } catch { }

        // Persist + probe
        _settings.Update(s => s.FfmpegPath = finalExe);
        var version = await ProbeVersionAsync(ct).ConfigureAwait(false) ?? "unknown";
        _settings.Update(s => s.FfmpegInstalledVersion = version);

        status?.Report($"ffmpeg {version} installed.");
        return finalExe;
    }

    private static string? ResolveFromPath()
    {
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* malformed PATH entry — skip */ }
        }
        return null;
    }

    private static string? ResolveFromManagedDir()
    {
        var binDir = Path.Combine(AppManagedDir, "bin");
        if (!Directory.Exists(binDir)) return null;
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var candidate = Path.Combine(binDir, name);
        return File.Exists(candidate) ? candidate : null;
    }

    private static (string Url, bool IsZip, string ExeRelative) ResolveDownloadInfo()
    {
        // Stable static-build URLs that don't require HTML scraping.
        if (OperatingSystem.IsWindows())
        {
            // gyan.dev essentials build (always-latest stable)
            return (
                "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
                IsZip: true,
                ExeRelative: "bin/ffmpeg.exe");
        }
        if (OperatingSystem.IsMacOS())
        {
            // evermeet.cx ships a single-file zip per binary
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "arm64" // evermeet currently ships universal x86_64 builds; arm64 falls back via Rosetta
                : "x86_64";
            _ = arch;
            return (
                "https://evermeet.cx/ffmpeg/getrelease/zip",
                IsZip: true,
                ExeRelative: "ffmpeg");
        }
        // Linux — johnvansickle.com static builds.
        var linuxUrl = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-arm64-static.tar.xz"
            : "https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz";
        return (linuxUrl, IsZip: false, ExeRelative: "ffmpeg");
    }

    private static string Host(string url)
    {
        try { return new Uri(url).Host; } catch { return url; }
    }
}

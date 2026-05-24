using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Pikura.Avalonia.Services;

/// <summary>
/// Service for generating and managing crash reports.
/// </summary>
public sealed class CrashReportService
{
    private readonly string _crashLogsFolder;
    private const string CrashFlagFile = "crash_detected.flag";
    private const string LastCrashInfoFile = "last_crash_info.txt";

    public CrashReportService()
    {
        _crashLogsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pikura",
            "CrashLogs");
        Directory.CreateDirectory(_crashLogsFolder);
    }

    /// <summary>
    /// Gets the path to the crash logs folder.
    /// </summary>
    public string CrashLogsFolder => _crashLogsFolder;

    /// <summary>
    /// Generates a comprehensive crash report.
    /// </summary>
    public string GenerateCrashReport(Exception exception, string? additionalContext = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var fileName = $"crash_{timestamp}.txt";
        var filePath = Path.Combine(_crashLogsFolder, fileName);

        var report = new StringBuilder();
        report.AppendLine("═══════════════════════════════════════════════════════════════");
        report.AppendLine("  PIKURA CRASH REPORT");
        report.AppendLine("═══════════════════════════════════════════════════════════════");
        report.AppendLine();
        report.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local)");
        report.AppendLine($"          {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        report.AppendLine();
        report.AppendLine("───────────────────────────────────────────────────────────────");
        report.AppendLine("  APPLICATION INFO");
        report.AppendLine("───────────────────────────────────────────────────────────────");
        report.AppendLine($"Application: Pikura");
        report.AppendLine($"Version: {GetAppVersion()}");
        report.AppendLine($"Build: {GetBuildInfo()}");
        report.AppendLine();
        report.AppendLine("───────────────────────────────────────────────────────────────");
        report.AppendLine("  SYSTEM INFO");
        report.AppendLine("───────────────────────────────────────────────────────────────");
        report.AppendLine($"OS: {RuntimeInformation.OSDescription}");
        report.AppendLine($"OS Architecture: {RuntimeInformation.OSArchitecture}");
        report.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
        report.AppendLine($"Process Architecture: {RuntimeInformation.ProcessArchitecture}");
        report.AppendLine($"Machine Name: {Environment.MachineName}");
        report.AppendLine($"User Name: {Environment.UserName}");
        report.AppendLine($"CLR Version: {Environment.Version}");
        report.AppendLine($"Working Set: {Environment.WorkingSet / 1024 / 1024} MB");
        report.AppendLine($"Processor Count: {Environment.ProcessorCount}");
        report.AppendLine();
        report.AppendLine("───────────────────────────────────────────────────────────────");
        report.AppendLine("  EXCEPTION DETAILS");
        report.AppendLine("───────────────────────────────────────────────────────────────");
        report.AppendLine($"Exception Type: {exception.GetType().FullName}");
        report.AppendLine($"Message: {exception.Message}");
        report.AppendLine($"Source: {exception.Source ?? "Unknown"}");
        report.AppendLine($"HResult: 0x{exception.HResult:X8}");
        report.AppendLine();
        report.AppendLine("Stack Trace:");
        report.AppendLine(exception.StackTrace ?? "No stack trace available");
        report.AppendLine();

        // Inner exception
        if (exception.InnerException != null)
        {
            report.AppendLine("───────────────────────────────────────────────────────────────");
            report.AppendLine("  INNER EXCEPTION");
            report.AppendLine("───────────────────────────────────────────────────────────────");
            report.AppendLine($"Type: {exception.InnerException.GetType().FullName}");
            report.AppendLine($"Message: {exception.InnerException.Message}");
            report.AppendLine($"Stack Trace:");
            report.AppendLine(exception.InnerException.StackTrace ?? "No stack trace available");
            report.AppendLine();
        }

        // Additional context
        if (!string.IsNullOrEmpty(additionalContext))
        {
            report.AppendLine("───────────────────────────────────────────────────────────────");
            report.AppendLine("  ADDITIONAL CONTEXT");
            report.AppendLine("───────────────────────────────────────────────────────────────");
            report.AppendLine(additionalContext);
            report.AppendLine();
        }

        report.AppendLine("───────────────────────────────────────────────────────────────");
        report.AppendLine("  LOADED ASSEMBLIES");
        report.AppendLine("───────────────────────────────────────────────────────────────");
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .OrderBy(a => a.FullName)
                .ToList();

            foreach (var asm in assemblies)
            {
                var name = asm.GetName();
                report.AppendLine($"  {name.Name} v{name.Version}");
            }
        }
        catch (Exception ex)
        {
            report.AppendLine($"  Failed to list assemblies: {ex.Message}");
        }

        report.AppendLine();
        report.AppendLine("═══════════════════════════════════════════════════════════════");
        report.AppendLine("  END OF CRASH REPORT");
        report.AppendLine("═══════════════════════════════════════════════════════════════");

        File.WriteAllText(filePath, report.ToString());

        // Write flag file to indicate crash occurred
        var flagPath = Path.Combine(_crashLogsFolder, CrashFlagFile);
        File.WriteAllText(flagPath, filePath);

        // Write simplified info for quick reading
        var infoPath = Path.Combine(_crashLogsFolder, LastCrashInfoFile);
        File.WriteAllText(infoPath, $"{timestamp}|{exception.GetType().Name}|{exception.Message}|{filePath}");

        return filePath;
    }

    /// <summary>
    /// Checks if a crash occurred in the previous session.
    /// </summary>
    public bool WasCrashDetected()
    {
        var flagPath = Path.Combine(_crashLogsFolder, CrashFlagFile);
        return File.Exists(flagPath);
    }

    /// <summary>
    /// Gets information about the last crash.
    /// </summary>
    public CrashInfo? GetLastCrashInfo()
    {
        var infoPath = Path.Combine(_crashLogsFolder, LastCrashInfoFile);
        if (!File.Exists(infoPath))
            return null;

        try
        {
            var content = File.ReadAllText(infoPath);
            var parts = content.Split('|');
            if (parts.Length >= 4)
            {
                return new CrashInfo
                {
                    Timestamp = DateTime.ParseExact(parts[0], "yyyy-MM-dd_HH-mm-ss", null),
                    ExceptionType = parts[1],
                    Message = parts[2],
                    LogFilePath = parts[3]
                };
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Clears the crash detected flag (call after showing crash dialog).
    /// </summary>
    public void ClearCrashFlag()
    {
        var flagPath = Path.Combine(_crashLogsFolder, CrashFlagFile);
        if (File.Exists(flagPath))
        {
            File.Delete(flagPath);
        }
    }

    /// <summary>
    /// Opens the crash logs folder in file explorer.
    /// </summary>
    public void OpenCrashLogsFolder()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _crashLogsFolder,
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Gets a list of all crash log files, sorted by date (newest first).
    /// </summary>
    public string[] GetAllCrashLogs()
    {
        return Directory.GetFiles(_crashLogsFolder, "crash_*.txt")
            .OrderByDescending(f => File.GetCreationTime(f))
            .ToArray();
    }

    private string GetAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "Unknown";
    }

    private string GetBuildInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var attribute = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
        return attribute?.Configuration ?? "Release";
    }
}

/// <summary>
/// Information about a crash.
/// </summary>
public sealed class CrashInfo
{
    public DateTime Timestamp { get; set; }
    public string ExceptionType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string LogFilePath { get; set; } = string.Empty;

    public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
}

using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Pixora.Avalonia.Services;

/// <summary>
/// Helper for managing Windows startup registry entries.
/// </summary>
public static class StartupHelper
{
    private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "PixivUtil2";

    /// <summary>
    /// Gets whether the app is set to start with Windows.
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
            var value = key?.GetValue(AppName);
            return value != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets whether the app should start with Windows.
    /// </summary>
    public static void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            if (key == null) return;

            if (enabled)
            {
                var exePath = GetExecutablePath();
                key.SetValue(AppName, exePath, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set startup: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the startup command with arguments based on startup settings.
    /// </summary>
    public static void UpdateStartupCommand(bool startMinimized, bool startInTray)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            if (key == null) return;

            // Check if we currently have a startup entry
            var existingValue = key.GetValue(AppName);
            if (existingValue == null) return; // Not set to start with Windows

            var exePath = GetExecutablePath();
            var args = BuildStartupArguments(startMinimized, startInTray);
            var command = args.Length > 0 ? $"\"{exePath}\" {args}" : exePath;

            key.SetValue(AppName, command, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to update startup command: {ex.Message}");
        }
    }

    private static string BuildStartupArguments(bool startMinimized, bool startInTray)
    {
        var args = "";
        if (startInTray)
        {
            args += "--tray ";
        }
        else if (startMinimized)
        {
            args += "--minimized ";
        }
        return args.Trim();
    }

    private static string GetExecutablePath()
    {
        var assembly = Assembly.GetEntryAssembly();
        if (assembly != null)
        {
            return assembly.Location;
        }

        // Fallback for published apps
        var processModule = Process.GetCurrentProcess().MainModule;
        if (processModule != null)
        {
            return processModule.FileName;
        }

        return Path.Combine(AppContext.BaseDirectory, "Pixora.Avalonia.exe");
    }
}

/// <summary>
/// Startup behavior options parsed from command line.
/// </summary>
public class StartupOptions
{
    /// <summary>Start minimized to taskbar.</summary>
    public bool Minimized { get; set; }

    /// <summary>Start minimized to system tray.</summary>
    public bool Tray { get; set; }

    /// <summary>Parse command line arguments.</summary>
    public static StartupOptions Parse(string[] args)
    {
        var options = new StartupOptions();

        foreach (var arg in args)
        {
            switch (arg.ToLowerInvariant())
            {
                case "--minimized":
                case "-min":
                    options.Minimized = true;
                    break;

                case "--tray":
                case "-tray":
                    options.Tray = true;
                    break;
            }
        }

        return options;
    }
}

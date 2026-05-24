using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;

namespace Pikura.Avalonia.Services;

/// <summary>
/// Writes log entries to a rolling text file in %AppData%\Pikura\pikura.log.
/// Rolls over at 5 MB, keeping one backup.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly LogLevel _minLevel;
    private readonly object _lock = new();
    private const long MaxBytes = 5 * 1024 * 1024; // 5 MB

    public FileLoggerProvider(string path, LogLevel minLevel)
    {
        _path = path;
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _path, _minLevel, _lock);

    public void Dispose() { }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _path;
        private readonly LogLevel _minLevel;
        private readonly object _lock;

        public FileLogger(string category, string path, LogLevel minLevel, object lockObj)
        {
            _category = category;
            _path = path;
            _minLevel = minLevel;
            _lock = lockObj;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var level = logLevel switch
            {
                LogLevel.Trace       => "TRC",
                LogLevel.Debug       => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning     => "WRN",
                LogLevel.Error       => "ERR",
                LogLevel.Critical    => "CRT",
                _                    => "???"
            };

            // Shorten category to last two segments for readability
            var parts = _category.Split('.');
            var cat = parts.Length > 2
                ? string.Join(".", parts[^2..])
                : _category;

            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{cat}] ");
            sb.Append(formatter(state, exception));
            if (exception != null)
                sb.Append($"\n{exception}");
            sb.AppendLine();

            lock (_lock)
            {
                try
                {
                    // Roll over if too large
                    if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                    {
                        var backup = _path + ".bak";
                        File.Copy(_path, backup, overwrite: true);
                        File.Delete(_path);
                    }
                    File.AppendAllText(_path, sb.ToString(), Encoding.UTF8);
                }
                catch { /* never crash the app due to logging */ }
            }
        }
    }
}

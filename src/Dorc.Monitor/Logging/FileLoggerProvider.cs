using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Dorc.Monitor.Logging
{
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _filePath;
        private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
        private readonly FileLoggerOptions _options;

        public FileLoggerProvider(string filePath, FileLoggerOptions options)
        {
            _filePath = filePath;
            _options = options;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _filePath, _options));
        }

        public void Dispose()
        {
            _loggers.Clear();
        }
    }

    public class FileLoggerOptions
    {
        public bool Append { get; set; } = true;
        public long FileSizeLimitBytes { get; set; } = 10 * 1024 * 1024; // 10MB default
        public int MaxRollingFiles { get; set; } = 100;
    }

    public class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _filePath;
        private readonly FileLoggerOptions _options;
        private static readonly object _lock = new object();

        public FileLogger(string categoryName, string filePath, FileLoggerOptions options)
        {
            _categoryName = categoryName;
            _filePath = filePath;
            _options = options;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            lock (_lock)
            {
                try
                {
                    var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevel,-11}] [{_categoryName}] - {formatter(state, exception)}";
                    if (exception != null)
                    {
                        logMessage += Environment.NewLine + exception.ToString();
                    }
                    logMessage += Environment.NewLine;

                    // Check file size and rotate if needed
                    if (File.Exists(_filePath))
                    {
                        var fileInfo = new FileInfo(_filePath);
                        if (fileInfo.Length >= _options.FileSizeLimitBytes)
                        {
                            RotateLogFile();
                        }
                    }

                    File.AppendAllText(_filePath, logMessage);
                }
                catch
                {
                    // Silently fail if unable to write to log file
                }
            }
        }

        private void RotateLogFile()
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                var fileName = Path.GetFileNameWithoutExtension(_filePath);
                var extension = Path.GetExtension(_filePath);
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd");
                
                // Find next available number
                int i = 1;
                string newFilePath;
                do
                {
                    newFilePath = Path.Combine(directory!, $"{fileName}.{timestamp}.{i}{extension}");
                    i++;
                } while (File.Exists(newFilePath) && i <= _options.MaxRollingFiles);

                if (i <= _options.MaxRollingFiles)
                {
                    File.Move(_filePath, newFilePath);
                }
                else
                {
                    // If we've exceeded max rolling files, just delete the oldest
                    File.Delete(_filePath);
                }
            }
            catch
            {
                // Silently fail if unable to rotate
            }
        }
    }

    public static class FileLoggerExtensions
    {
        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string filePath, Action<FileLoggerOptions>? configure = null)
        {
            var options = new FileLoggerOptions();
            configure?.Invoke(options);
            
            builder.AddProvider(new FileLoggerProvider(filePath, options));
            return builder;
        }
    }
}

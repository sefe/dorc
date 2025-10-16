using System;
using log4net;
using Microsoft.Extensions.Logging;

namespace Dorc.Monitor.Logging
{
    /// <summary>
    /// Bridges Microsoft.Extensions.Logging to an injected log4net ILog instance.
    /// Filtering is delegated to log4net (IsXEnabled checks) so log4net configuration
    /// controls the effective level.
    /// </summary>
    internal sealed class Log4NetLoggerProvider : ILoggerProvider
    {
        private readonly ILog _target;
        private readonly bool _includeCategory;

        public Log4NetLoggerProvider(ILog target, bool includeCategory = true)
        {
            _target = target;
            _includeCategory = includeCategory;
        }

        public ILogger CreateLogger(string categoryName) => new ForwardingLogger(_target, categoryName, _includeCategory);

        public void Dispose() { }

        private sealed class ForwardingLogger : ILogger
        {
            private readonly ILog _log;
            private readonly string _category;
            private readonly bool _includeCategory;

            public ForwardingLogger(ILog log, string category, bool includeCategory)
            {
                _log = log;
                _category = category;
                _includeCategory = includeCategory;
            }

            public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => logLevel switch
            {
                LogLevel.Trace or LogLevel.Debug => _log.IsDebugEnabled,
                LogLevel.Information => _log.IsInfoEnabled,
                LogLevel.Warning => _log.IsWarnEnabled,
                LogLevel.Error => _log.IsErrorEnabled,
                LogLevel.Critical => _log.IsFatalEnabled,
                LogLevel.None => false,
                _ => true
            };

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                string message = formatter(state, exception);
                if (_includeCategory)
                {
                    message = $"[{_category}] {message}";
                }

                switch (logLevel)
                {
                    case LogLevel.Trace:
                    case LogLevel.Debug:
                        _log.Debug(message, exception);
                        break;
                    case LogLevel.Information:
                        _log.Info(message, exception);
                        break;
                    case LogLevel.Warning:
                        _log.Warn(message, exception);
                        break;
                    case LogLevel.Error:
                        _log.Error(message, exception);
                        break;
                    case LogLevel.Critical:
                        _log.Fatal(message, exception);
                        break;
                }
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}

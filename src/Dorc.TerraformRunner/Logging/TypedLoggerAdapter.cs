using Microsoft.Extensions.Logging;

namespace Dorc.TerraformRunner.Logging
{
    /// <summary>
    /// Adapts the runner's single non-generic <see cref="ILogger"/> (from
    /// IRunnerLogger.FileLogger) to the <see cref="ILogger{T}"/> that
    /// component constructors expect, so their diagnostics reach the runner
    /// log file instead of being dropped into NullLogger.
    /// </summary>
    public sealed class TypedLoggerAdapter<T> : ILogger<T>
    {
        private readonly ILogger _inner;

        public TypedLoggerAdapter(ILogger inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}

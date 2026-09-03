using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Client.Tests.Consumers;

internal sealed class CapturingLogger : ILogger
{
    public List<LogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var entry = new LogEntry { Level = logLevel, RenderedMessage = formatter(state, exception) };
        if (state is IEnumerable<KeyValuePair<string, object?>> kv)
        {
            foreach (var pair in kv)
            {
                if (pair.Key == "{OriginalFormat}")
                    entry.MessageTemplate = pair.Value?.ToString();
                else
                    entry.Properties[pair.Key] = pair.Value;
            }
        }
        Entries.Add(entry);
    }
}

internal sealed class LogEntry
{
    public LogLevel Level { get; set; }

    public string? MessageTemplate { get; set; }

    public string RenderedMessage { get; set; } = string.Empty;

    public Dictionary<string, object?> Properties { get; } = new();
}

using Confluent.Kafka;
using Dorc.Kafka.Client.Consumers;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Client.IntegrationTests;

[TestClass]
public class RebalanceTests
{
    // AT-3 live: two consumers join the same group against a 4-partition topic;
    // both emit rebalance entries with the §4.3 shape contract.

    [TestMethod]
    public async Task TwoConsumers_JoiningSameGroup_EmitRebalanceEntriesOnBothSides()
    {
        var topic = KafkaTestHarness.NewTopicName("at3-rebalance");
        await KafkaTestHarness.CreateTopicAsync(topic, partitions: 4);
        try
        {
            var options = KafkaTestHarness.DefaultOptions(groupId: $"rebalance-{Guid.NewGuid():N}");
            var loggerA = new RecordingLogger();
            var loggerB = new RecordingLogger();

            using var consumerA = KafkaTestHarness
                .ConsumerBuilder<string, byte[]>(options, Wrap<KafkaConsumerBuilder<string, byte[]>>(loggerA))
                .Build("consumer-a");
            consumerA.Subscribe(topic);

            await PumpFor(new[] { consumerA }, TimeSpan.FromSeconds(8));
            Assert.IsTrue(consumerA.Assignment.Count > 0, "consumer-a did not receive any partitions before B joined");

            using var consumerB = KafkaTestHarness
                .ConsumerBuilder<string, byte[]>(options, Wrap<KafkaConsumerBuilder<string, byte[]>>(loggerB))
                .Build("consumer-b");
            consumerB.Subscribe(topic);

            await PumpFor(new[] { consumerA, consumerB }, TimeSpan.FromSeconds(15));

            AssertAssignedShape(loggerA, "consumer-a");
            AssertAssignedShape(loggerB, "consumer-b");
            // At least one side must show an 'incrementally revoked' entry as B's arrival
            // triggers cooperative movement off A (or, in rare schedules, off B after a second rebalance).
            var revokedSeen = loggerA.Entries.Any(IsRevoked) || loggerB.Entries.Any(IsRevoked);
            Assert.IsTrue(revokedSeen, "Neither consumer emitted a 'incrementally revoked' entry.");
        }
        finally
        {
            await KafkaTestHarness.DeleteTopicAsync(topic);
        }
    }

    private static bool IsRevoked(Recorded e)
        => e.Template.Contains("incrementally revoked") && e.Level == LogLevel.Information;

    private static void AssertAssignedShape(RecordingLogger logger, string consumerName)
    {
        var assigned = logger.Entries
            .Where(e => e.Template.Contains("incrementally assigned"))
            .ToList();

        Assert.IsTrue(assigned.Count > 0, $"{consumerName} emitted no 'incrementally assigned' entry");
        var first = assigned[0];
        Assert.AreEqual(LogLevel.Information, first.Level);
        Assert.AreEqual(consumerName, first.Properties["ConsumerName"]);
        Assert.IsTrue(first.Properties.ContainsKey("AssignedPartitions"), "AssignedPartitions param missing");
        Assert.IsTrue(first.Properties.ContainsKey("AllPartitions"), "AllPartitions param missing");
    }

    private static async Task PumpFor(IEnumerable<IConsumer<string, byte[]>> consumers, TimeSpan duration)
    {
        var list = consumers.ToList();
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var c in list)
            {
                _ = c.Consume(TimeSpan.FromMilliseconds(200));
            }
            await Task.Yield();
        }
    }

    private static ILogger<T> Wrap<T>(RecordingLogger sink) => new TypedLogger<T>(sink);

    private sealed class TypedLogger<T> : ILogger<T>
    {
        private readonly RecordingLogger _inner;
        public TypedLogger(RecordingLogger inner) => _inner = inner;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}

internal sealed class RecordingLogger : ILogger
{
    private readonly object _gate = new();
    public List<Recorded> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var rec = new Recorded { Level = logLevel, Rendered = formatter(state, exception) };
        if (state is IEnumerable<KeyValuePair<string, object?>> kv)
        {
            foreach (var pair in kv)
            {
                if (pair.Key == "{OriginalFormat}") rec.Template = pair.Value?.ToString() ?? string.Empty;
                else rec.Properties[pair.Key] = pair.Value;
            }
        }
        lock (_gate) Entries.Add(rec);
    }
}

internal sealed class Recorded
{
    public LogLevel Level { get; set; }
    public string Template { get; set; } = string.Empty;
    public string Rendered { get; set; } = string.Empty;
    public Dictionary<string, object?> Properties { get; } = new();
}

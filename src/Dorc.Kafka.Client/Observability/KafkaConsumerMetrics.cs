using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dorc.Kafka.Client.Observability;

/// <summary>
/// Parses Confluent.Kafka's statistics JSON and surfaces consumer lag and
/// state through <c>System.Diagnostics.Metrics</c>. The metric meter is
/// named <see cref="MeterName"/>; downstream OpenTelemetry export is wired
/// by the host application (e.g. <c>builder.Services.AddOpenTelemetry().WithMetrics(b =&gt; b.AddMeter(KafkaConsumerMetrics.MeterName))</c>).
///
/// Lag is published as an observable gauge tagged by <c>consumer</c>,
/// <c>group</c>, <c>topic</c>, and <c>partition</c> — so a single replica
/// running multiple consumer types and subscribed to multiple topics
/// produces distinguishable time-series. The gauge callback enumerates
/// the latest snapshot of <see cref="_lagByPartition"/>; stats updates
/// replace the snapshot atomically.
///
/// Consumer state is published as a separate gauge encoded numerically
/// (0=down, 1=init, 2=connecting, 3=up, 4=consuming) so dashboards can
/// alert on <c>state &lt; 3</c> without parsing strings.
/// </summary>
public sealed class KafkaConsumerMetrics : IKafkaConsumerMetrics, IDisposable
{
    public const string MeterName = "Dorc.Kafka.Consumer";

    private readonly Meter _meter;
    private readonly ILogger _logger;

    // (consumer, group, topic, partition) → lag value. Read from gauge
    // callbacks (which can fire on any thread); write from the consumer's
    // statistics callback. ConcurrentDictionary handles both safely.
    private readonly ConcurrentDictionary<MetricKey, long> _lagByPartition = new();
    // Unlike lag (per-partition, evicted via ForgetPartitions on rebalance),
    // state entries are keyed by logical consumer name. Names are static per
    // consumer type in this codebase, so an in-process consumer rebuild
    // overwrites its entry rather than orphaning it, and entries only become
    // stale at process shutdown when the Meter dies anyway. If dynamically
    // named consumers are ever introduced, add an eviction hook first.
    private readonly ConcurrentDictionary<string, int> _stateByConsumer = new();

    public KafkaConsumerMetrics(ILogger<KafkaConsumerMetrics>? logger = null)
    {
        _meter = new Meter(MeterName);
        _logger = (ILogger?)logger ?? NullLogger.Instance;

        _meter.CreateObservableGauge(
            "kafka.consumer.lag",
            ObserveLag,
            unit: "messages",
            description: "Consumer lag (committed-end-offset minus consumed-end-offset) per topic-partition.");

        _meter.CreateObservableGauge(
            "kafka.consumer.state",
            ObserveState,
            unit: "1",
            description: "Consumer state (0=down, 1=init, 2=connecting, 3=up, 4=consuming).");
    }

    public void ForgetPartitions(string consumerName, IEnumerable<Confluent.Kafka.TopicPartition> partitions)
    {
        // CooperativeSticky rebalancing reassigns partitions across replicas
        // throughout a process's lifetime. Without this eviction step, every
        // partition this replica ever owned remains in _lagByPartition with
        // its last-observed lag, exported as a stale metric on every scrape.
        // The group dimension is unknown at revocation time (librdkafka doesn't
        // pass it through), so we match (consumer, topic, partition) tuples
        // regardless of group.
        foreach (var tp in partitions)
        {
            // Snapshot keys to a list before removing — modifying the dictionary
            // during enumeration of .Keys would otherwise risk InvalidOperationException
            // even though ConcurrentDictionary tolerates it. The Where filter makes the
            // matching condition explicit and removes the dead `_ = key` placeholder.
            var matching = _lagByPartition.Keys
                .Where(k => k.Consumer == consumerName
                            && k.Topic == tp.Topic
                            && k.Partition == tp.Partition.Value)
                .ToList();
            foreach (var stored in matching)
                _lagByPartition.TryRemove(stored, out _);
        }
    }

    public void RecordStatistics(string consumerName, string statsJson)
    {
        if (string.IsNullOrEmpty(statsJson)) return;

        try
        {
            using var doc = JsonDocument.Parse(statsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.String)
            {
                _stateByConsumer[consumerName] = StateToCode(stateProp.GetString());
            }

            if (!root.TryGetProperty("topics", out var topics) || topics.ValueKind != JsonValueKind.Object)
                return;

            // librdkafka stores group id at the top level (ignored here in
            // favour of the consumer-supplied logical name) and per-partition
            // lag under topics.{topic}.partitions.{partition}.consumer_lag.
            // The "-1" partition entry is librdkafka's internal aggregate;
            // skip it so dashboards don't see a misleading sentinel.
            string groupId = root.TryGetProperty("cgrp", out var cgrp)
                && cgrp.ValueKind == JsonValueKind.Object
                && cgrp.TryGetProperty("group_id", out var gid)
                && gid.ValueKind == JsonValueKind.String
                ? gid.GetString() ?? string.Empty
                : string.Empty;

            foreach (var topic in topics.EnumerateObject())
            {
                if (!topic.Value.TryGetProperty("partitions", out var partitions)) continue;
                foreach (var partition in partitions.EnumerateObject())
                {
                    if (!int.TryParse(partition.Name, out var partitionNum) || partitionNum < 0) continue;
                    if (!partition.Value.TryGetProperty("consumer_lag", out var lagProp)) continue;
                    if (!lagProp.TryGetInt64(out var lag) || lag < 0) continue;

                    var key = new MetricKey(consumerName, groupId, topic.Name, partitionNum);
                    _lagByPartition[key] = lag;
                }
            }
        }
        catch (JsonException ex)
        {
            // Malformed payload from librdkafka shouldn't crash the consume loop — but it
            // also shouldn't be silent: parse failures stop lag/state from being published
            // for the affected consumer until the next stats tick succeeds, so dashboards
            // would see flat-lining metrics with no other signal. Log at Warning so the
            // problem is visible to operators without being lost in Debug noise.
            _logger.LogWarning(ex, "kafka-metrics-parse-failed consumer={Consumer}", consumerName);
        }
    }

    private IEnumerable<Measurement<long>> ObserveLag()
    {
        foreach (var kvp in _lagByPartition)
        {
            yield return new Measurement<long>(kvp.Value,
                new KeyValuePair<string, object?>("consumer", kvp.Key.Consumer),
                new KeyValuePair<string, object?>("group", kvp.Key.Group),
                new KeyValuePair<string, object?>("topic", kvp.Key.Topic),
                new KeyValuePair<string, object?>("partition", kvp.Key.Partition));
        }
    }

    private IEnumerable<Measurement<int>> ObserveState()
    {
        foreach (var kvp in _stateByConsumer)
        {
            yield return new Measurement<int>(kvp.Value,
                new KeyValuePair<string, object?>("consumer", kvp.Key));
        }
    }

    private static int StateToCode(string? state) => state?.ToLowerInvariant() switch
    {
        "down" => 0,
        "init" => 1,
        "connect" or "connecting" => 2,
        "up" => 3,
        "consuming" or "active" => 4,
        _ => -1
    };

    public void Dispose() => _meter.Dispose();

    private readonly record struct MetricKey(string Consumer, string Group, string Topic, int Partition);
}

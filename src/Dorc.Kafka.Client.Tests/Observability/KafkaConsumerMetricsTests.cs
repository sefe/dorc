using System.Diagnostics.Metrics;
using Dorc.Kafka.Client.Observability;

namespace Dorc.Kafka.Client.Tests.Observability;

[TestClass]
public class KafkaConsumerMetricsTests
{
    [TestMethod]
    public void RecordStatistics_ParsesPerPartitionLag_AndPublishesViaObservableGauge()
    {
        // Sample shape mirrors librdkafka's stats payload: per-topic
        // partition map with consumer_lag. Negative partitions (-1) are
        // librdkafka's internal aggregate and must be skipped.
        const string statsJson = """
            {
                "name": "rdkafka#consumer-1",
                "state": "up",
                "cgrp": { "group_id": "test-group" },
                "topics": {
                    "dorc.results.status": {
                        "partitions": {
                            "-1": { "partition": -1, "consumer_lag": 0 },
                            "0":  { "partition": 0,  "consumer_lag": 7 },
                            "1":  { "partition": 1,  "consumer_lag": 0 },
                            "2":  { "partition": 2,  "consumer_lag": 99 }
                        }
                    }
                }
            }
            """;

        using var listener = new MeterListener();
        var measurements = new List<(long value, IDictionary<string, object?> tags)>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == KafkaConsumerMetrics.MeterName &&
                instrument.Name == "kafka.consumer.lag")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var copy = new Dictionary<string, object?>();
            foreach (var t in tags) copy[t.Key] = t.Value;
            measurements.Add((value, copy));
        });
        listener.Start();

        using var metrics = new KafkaConsumerMetrics();
        metrics.RecordStatistics("results-consumer", statsJson);
        listener.RecordObservableInstruments();

        // Three valid partitions; the -1 sentinel is filtered out.
        Assert.AreEqual(3, measurements.Count, $"Expected 3 partitions, got {measurements.Count}");

        var byPartition = measurements.ToDictionary(
            m => (int)m.tags["partition"]!,
            m => (m.value, m.tags));

        Assert.AreEqual(7L, byPartition[0].value);
        Assert.AreEqual(0L, byPartition[1].value);
        Assert.AreEqual(99L, byPartition[2].value);

        Assert.AreEqual("results-consumer", byPartition[0].tags["consumer"]);
        Assert.AreEqual("test-group", byPartition[0].tags["group"]);
        Assert.AreEqual("dorc.results.status", byPartition[0].tags["topic"]);
    }

    [TestMethod]
    public void RecordStatistics_MalformedJson_DoesNotThrow_AndPublishesNothing()
    {
        // librdkafka shouldn't ever emit malformed JSON, but a parse failure
        // must not crash the consume thread.
        using var listener = new MeterListener();
        var measurements = 0;
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == KafkaConsumerMetrics.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => measurements++);
        listener.Start();

        using var metrics = new KafkaConsumerMetrics();
        metrics.RecordStatistics("c", "{ this is not json");
        listener.RecordObservableInstruments();

        Assert.AreEqual(0, measurements);
    }

    [TestMethod]
    public void ForgetPartitions_EvictsLagEntriesForRevokedPartitions()
    {
        // CooperativeSticky rebalance: the consumer kept partition 0, lost
        // partition 1. Lag for partition 1 must disappear from the gauge so
        // dashboards don't alert on a partition this replica no longer owns.
        const string statsJson = """
            {
                "name": "rdkafka#consumer-1",
                "state": "up",
                "topics": {
                    "dorc.results.status": {
                        "partitions": {
                            "0": { "partition": 0, "consumer_lag": 5 },
                            "1": { "partition": 1, "consumer_lag": 12 }
                        }
                    }
                }
            }
            """;

        using var listener = new MeterListener();
        var measurements = new List<(long value, IDictionary<string, object?> tags)>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == KafkaConsumerMetrics.MeterName &&
                instrument.Name == "kafka.consumer.lag")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var copy = new Dictionary<string, object?>();
            foreach (var t in tags) copy[t.Key] = t.Value;
            measurements.Add((value, copy));
        });
        listener.Start();

        using var metrics = new KafkaConsumerMetrics();
        metrics.RecordStatistics("c", statsJson);

        // Revoke partition 1. The remaining gauge collection should publish
        // partition 0 only; partition 1's stale lag must be evicted.
        metrics.ForgetPartitions("c", new[]
        {
            new Confluent.Kafka.TopicPartition("dorc.results.status", 1)
        });

        listener.RecordObservableInstruments();

        Assert.AreEqual(1, measurements.Count, "Only partition 0 should remain after revocation.");
        Assert.AreEqual(0, (int)measurements[0].tags["partition"]!);
        Assert.AreEqual(5L, measurements[0].value);
    }

    [DataTestMethod]
    [DataRow("down", 0)]
    [DataRow("init", 1)]
    [DataRow("connect", 2)]
    [DataRow("connecting", 2)]
    [DataRow("up", 3)]
    [DataRow("consuming", 4)]
    [DataRow("active", 4)]
    [DataRow("rebalance", -1)]      // unknown librdkafka state -> sentinel
    [DataRow("WaitAssignor", -1)]   // case-folded fallback
    public void RecordStatistics_EncodesConsumerStateNumerically(string stateString, int expectedCode)
    {
        // Pin the full numeric encoding so a future refactor can't silently
        // shift code values out from under dashboards alerting on `state < 3`.
        var json = $$"""{ "state": "{{stateString}}" }""";

        using var listener = new MeterListener();
        int? observed = null;
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == KafkaConsumerMetrics.MeterName &&
                instrument.Name == "kafka.consumer.state")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((_, value, _, _) => observed = value);
        listener.Start();

        using var metrics = new KafkaConsumerMetrics();
        metrics.RecordStatistics("c", json);
        listener.RecordObservableInstruments();

        Assert.AreEqual(expectedCode, observed,
            $"State '{stateString}' should map to code {expectedCode}.");
    }
}

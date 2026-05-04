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
    public void RecordStatistics_EncodesConsumerStateNumerically()
    {
        const string upJson = """{ "state": "up" }""";
        const string downJson = """{ "state": "down" }""";

        using var listener = new MeterListener();
        var stateValues = new Dictionary<string, int>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == KafkaConsumerMetrics.MeterName &&
                instrument.Name == "kafka.consumer.state")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
        {
            string consumer = "";
            foreach (var t in tags)
                if (t.Key == "consumer") consumer = (string)t.Value!;
            stateValues[consumer] = value;
        });
        listener.Start();

        using var metrics = new KafkaConsumerMetrics();
        metrics.RecordStatistics("up-consumer", upJson);
        metrics.RecordStatistics("down-consumer", downJson);
        listener.RecordObservableInstruments();

        Assert.AreEqual(3, stateValues["up-consumer"]);
        Assert.AreEqual(0, stateValues["down-consumer"]);
    }
}

using Confluent.Kafka;
using Dorc.Kafka.Client.Consumers;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Client.Tests.Consumers;

[TestClass]
public class KafkaRebalanceHandlersTests
{
    private const string Topic = "dorc.test";

    [TestMethod]
    public void OnPartitionsAssigned_LogsInformationWithDeltaAndSteadyState()
    {
        var logger = new CapturingLogger();
        var handlers = new KafkaRebalanceHandlers<string, byte[]>(logger, "monitor-a");
        var existing = new[] { new TopicPartition(Topic, 0) };
        var consumer = new StubConsumer<string, byte[]>(existing);
        var incoming = new List<TopicPartition>
        {
            new(Topic, 1),
            new(Topic, 2)
        };

        handlers.OnPartitionsAssigned(consumer, incoming);

        var entry = logger.Entries.Single();
        Assert.AreEqual(LogLevel.Information, entry.Level);
        StringAssert.Contains(entry.MessageTemplate!, "incrementally assigned");
        StringAssert.Contains(entry.MessageTemplate!, "{AssignedPartitions}");
        StringAssert.Contains(entry.MessageTemplate!, "{AllPartitions}");
        Assert.AreEqual("monitor-a", entry.Properties["ConsumerName"]);
        Assert.AreEqual("1,2", entry.Properties["AssignedPartitions"]);
        Assert.AreEqual("0,1,2", entry.Properties["AllPartitions"]);
    }

    [TestMethod]
    public void OnPartitionsRevoked_LogsInformationWithDeltaAndRemaining()
    {
        var logger = new CapturingLogger();
        var handlers = new KafkaRebalanceHandlers<string, byte[]>(logger, "monitor-b");
        var current = new[]
        {
            new TopicPartition(Topic, 0),
            new TopicPartition(Topic, 1),
            new TopicPartition(Topic, 2)
        };
        var consumer = new StubConsumer<string, byte[]>(current);
        var revoked = new List<TopicPartitionOffset>
        {
            new(new TopicPartition(Topic, 1), Offset.Unset)
        };

        handlers.OnPartitionsRevoked(consumer, revoked);

        var entry = logger.Entries.Single();
        Assert.AreEqual(LogLevel.Information, entry.Level);
        StringAssert.Contains(entry.MessageTemplate!, "incrementally revoked");
        StringAssert.Contains(entry.MessageTemplate!, "{RevokedPartitions}");
        StringAssert.Contains(entry.MessageTemplate!, "{RemainingPartitions}");
        Assert.AreEqual("monitor-b", entry.Properties["ConsumerName"]);
        Assert.AreEqual("1", entry.Properties["RevokedPartitions"]);
        Assert.AreEqual("0,2", entry.Properties["RemainingPartitions"]);
    }

    [TestMethod]
    public void OnPartitionsLost_LogsWarningWithLostList()
    {
        var logger = new CapturingLogger();
        var handlers = new KafkaRebalanceHandlers<string, byte[]>(logger, "monitor-c");
        var consumer = new StubConsumer<string, byte[]>();
        var lost = new List<TopicPartitionOffset>
        {
            new(new TopicPartition(Topic, 3), Offset.Unset),
            new(new TopicPartition(Topic, 4), Offset.Unset)
        };

        handlers.OnPartitionsLost(consumer, lost);

        var entry = logger.Entries.Single();
        Assert.AreEqual(LogLevel.Warning, entry.Level);
        StringAssert.Contains(entry.MessageTemplate!, "were lost");
        StringAssert.Contains(entry.MessageTemplate!, "{LostPartitions}");
        Assert.AreEqual("monitor-c", entry.Properties["ConsumerName"]);
        Assert.AreEqual("3,4", entry.Properties["LostPartitions"]);
    }

    [TestMethod]
    public void OnPartitionsAssigned_EmptyDelta_StillLogsWithShape()
    {
        // Cooperative-sticky edge case covered in AT-3: empty deltas are permitted
        // and must still be logged with the §4.3 shape.
        var logger = new CapturingLogger();
        var handlers = new KafkaRebalanceHandlers<string, byte[]>(logger, "monitor-d");
        var consumer = new StubConsumer<string, byte[]>(new[] { new TopicPartition(Topic, 0) });

        handlers.OnPartitionsAssigned(consumer, new List<TopicPartition>());

        var entry = logger.Entries.Single();
        Assert.AreEqual(LogLevel.Information, entry.Level);
        Assert.AreEqual(string.Empty, entry.Properties["AssignedPartitions"]);
        Assert.AreEqual("0", entry.Properties["AllPartitions"]);
    }

    [TestMethod]
    public void OnError_LogsErrorWithReasonCodeFatal()
    {
        var logger = new CapturingLogger();
        var handlers = new KafkaRebalanceHandlers<string, byte[]>(logger, "monitor-e");
        var consumer = new StubConsumer<string, byte[]>();

        handlers.OnError(consumer, new Error(ErrorCode.BrokerNotAvailable, "broker gone", isFatal: false));

        var entry = logger.Entries.Single();
        Assert.AreEqual(LogLevel.Error, entry.Level);
        Assert.AreEqual("broker gone", entry.Properties["Reason"]);
        Assert.AreEqual(ErrorCode.BrokerNotAvailable, entry.Properties["Code"]);
        Assert.IsFalse((bool)entry.Properties["Fatal"]!);
    }
}

using Dorc.PersistentData.Model;

namespace Dorc.Kafka.ErrorLog.Tests;

[TestClass]
public class KafkaErrorLogTests
{
    // ---- AT-2: Insert round-trip + truncation ----

    [TestMethod]
    public async Task Insert_AllFieldsPopulated_RoundTripsByteEqual()
    {
        using var harness = new InMemoryTestHarness();
        var occurredAt = DateTimeOffset.Parse("2026-04-14T10:00:00Z");
        var entry = new KafkaErrorLogEntry
        {
            Topic = "dorc.requests.new",
            Partition = 7,
            Offset = 12345,
            ConsumerGroup = "dorc-monitor",
            MessageKey = "42",
            RawPayload = new byte[] { 1, 2, 3, 4, 5 },
            Error = "Avro deserialisation failed",
            Stack = "at Confluent.Kafka...",
            OccurredAt = occurredAt
        };

        await harness.DAL.InsertAsync(entry, CancellationToken.None);

        using var ctx = harness.NewContext();
        var stored = ctx.KafkaErrorLogEntries.Single();
        Assert.AreEqual("dorc.requests.new", stored.Topic);
        Assert.AreEqual(7, stored.Partition);
        Assert.AreEqual(12345L, stored.Offset);
        Assert.AreEqual("dorc-monitor", stored.ConsumerGroup);
        Assert.AreEqual("42", stored.MessageKey);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, stored.RawPayload);
        Assert.IsFalse(stored.PayloadTruncated);
        Assert.AreEqual("Avro deserialisation failed", stored.Error);
        Assert.AreEqual("at Confluent.Kafka...", stored.Stack);
        Assert.AreEqual(occurredAt, stored.OccurredAt);
        Assert.IsTrue(stored.LoggedAt > DateTimeOffset.UtcNow.AddSeconds(-10));
    }

    [TestMethod]
    public async Task Insert_OnlyMandatoryFields_NullablesLandAsNull()
    {
        using var harness = new InMemoryTestHarness();
        var entry = new KafkaErrorLogEntry
        {
            Topic = "t",
            Partition = 0,
            Offset = 1,
            ConsumerGroup = "g",
            Error = "boom",
            OccurredAt = DateTimeOffset.UtcNow
        };

        await harness.DAL.InsertAsync(entry, CancellationToken.None);

        using var ctx = harness.NewContext();
        var stored = ctx.KafkaErrorLogEntries.Single();
        Assert.IsNull(stored.MessageKey);
        Assert.IsNull(stored.RawPayload);
        Assert.IsNull(stored.Stack);
    }

    [TestMethod]
    public async Task Insert_PayloadStrictlyExceedsMax_TruncatedToMax_FlagSet()
    {
        using var harness = new InMemoryTestHarness(new KafkaErrorLogOptions { MaxPayloadBytes = 8 });
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        await harness.DAL.InsertAsync(NewEntry(payload), CancellationToken.None);

        using var ctx = harness.NewContext();
        var stored = ctx.KafkaErrorLogEntries.Single();
        Assert.IsTrue(stored.PayloadTruncated);
        Assert.AreEqual(8, stored.RawPayload!.Length);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, stored.RawPayload);
    }

    [TestMethod]
    public async Task Insert_PayloadEqualToMax_StoredIntact_FlagFalse()
    {
        using var harness = new InMemoryTestHarness(new KafkaErrorLogOptions { MaxPayloadBytes = 8 });
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await harness.DAL.InsertAsync(NewEntry(payload), CancellationToken.None);

        using var ctx = harness.NewContext();
        var stored = ctx.KafkaErrorLogEntries.Single();
        Assert.IsFalse(stored.PayloadTruncated);
        CollectionAssert.AreEqual(payload, stored.RawPayload);
    }

    // ---- AT-4: Query filters ----

    [TestMethod]
    public async Task Query_FilterByTopic_ReturnsOnlyMatchingTopic()
    {
        using var harness = new InMemoryTestHarness();
        await Seed(harness, ("topic-a", "g1", DateTimeOffset.UtcNow));
        await Seed(harness, ("topic-b", "g1", DateTimeOffset.UtcNow));

        var rows = await harness.DAL.QueryAsync("topic-a", null, null, 100, CancellationToken.None);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("topic-a", rows[0].Topic);
    }

    [TestMethod]
    public async Task Query_FilterByConsumerGroup_ReturnsOnlyMatchingGroup()
    {
        using var harness = new InMemoryTestHarness();
        await Seed(harness, ("t", "g1", DateTimeOffset.UtcNow));
        await Seed(harness, ("t", "g2", DateTimeOffset.UtcNow));

        var rows = await harness.DAL.QueryAsync(null, "g2", null, 100, CancellationToken.None);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("g2", rows[0].ConsumerGroup);
    }

    [TestMethod]
    public async Task Query_FilterBySinceUtc_ReturnsOnlyAfterCutoff()
    {
        using var harness = new InMemoryTestHarness();
        var now = DateTimeOffset.UtcNow;
        await Seed(harness, ("t", "g", now.AddHours(-2)));
        await Seed(harness, ("t", "g", now.AddMinutes(-10)));

        var rows = await harness.DAL.QueryAsync(null, null, now.AddHours(-1), 100, CancellationToken.None);

        Assert.AreEqual(1, rows.Count);
    }

    [TestMethod]
    public async Task Query_MaxRows_CapsResults()
    {
        using var harness = new InMemoryTestHarness();
        for (var i = 0; i < 10; i++)
            await Seed(harness, ("t", "g", DateTimeOffset.UtcNow.AddSeconds(-i)));

        var rows = await harness.DAL.QueryAsync(null, null, null, 3, CancellationToken.None);

        Assert.AreEqual(3, rows.Count);
    }

    [TestMethod]
    public async Task Query_MaxRows_CappedByOptions()
    {
        using var harness = new InMemoryTestHarness(new KafkaErrorLogOptions { QueryMaxRowsCap = 5 });
        for (var i = 0; i < 10; i++)
            await Seed(harness, ("t", "g", DateTimeOffset.UtcNow.AddSeconds(-i)));

        var rows = await harness.DAL.QueryAsync(null, null, null, 100, CancellationToken.None);

        Assert.AreEqual(5, rows.Count);
    }

    [TestMethod]
    public async Task Query_OrderedByOccurredAtDescThenIdDesc()
    {
        using var harness = new InMemoryTestHarness();
        var t = DateTimeOffset.UtcNow;
        // Insert two with the same OccurredAt to exercise the Id tiebreaker.
        await Seed(harness, ("t", "g", t.AddSeconds(-10)));
        await Seed(harness, ("t", "g", t));
        await Seed(harness, ("t", "g", t));

        var rows = await harness.DAL.QueryAsync(null, null, null, 100, CancellationToken.None);

        Assert.AreEqual(3, rows.Count);
        Assert.IsTrue(rows[0].OccurredAt >= rows[1].OccurredAt);
        Assert.IsTrue(rows[1].OccurredAt >= rows[2].OccurredAt);
        // Id tiebreaker between rows[0] and rows[1] (both at t).
        Assert.IsTrue(rows[0].Id > rows[1].Id);
    }

    // ---- AT-5: Purge ----

    [TestMethod]
    public async Task Purge_DeletesOnlyOlderThanRetention()
    {
        using var harness = new InMemoryTestHarness(new KafkaErrorLogOptions { RetentionDays = 30 });
        var now = DateTimeOffset.UtcNow;
        await Seed(harness, ("t", "g", now.AddDays(-31)));   // delete
        await Seed(harness, ("t", "g", now.AddDays(-45)));   // delete
        await Seed(harness, ("t", "g", now.AddDays(-29)));   // keep
        await Seed(harness, ("t", "g", now.AddDays(-1)));    // keep

        var deleted = await harness.DAL.PurgeAsync(CancellationToken.None);

        Assert.AreEqual(2, deleted);
        using var ctx = harness.NewContext();
        Assert.AreEqual(2, ctx.KafkaErrorLogEntries.Count());
    }

    [TestMethod]
    public async Task Purge_SecondCall_ReturnsZero()
    {
        using var harness = new InMemoryTestHarness(new KafkaErrorLogOptions { RetentionDays = 30 });
        await Seed(harness, ("t", "g", DateTimeOffset.UtcNow.AddDays(-100)));

        await harness.DAL.PurgeAsync(CancellationToken.None);
        var second = await harness.DAL.PurgeAsync(CancellationToken.None);

        Assert.AreEqual(0, second);
    }

    [TestMethod]
    public async Task Purge_AcrossMultipleBatches_DeletesAll()
    {
        using var harness = new InMemoryTestHarness(new KafkaErrorLogOptions
        {
            RetentionDays = 30,
            PurgeBatchSize = 5
        });
        for (var i = 0; i < 12; i++)
            await Seed(harness, ("t", "g", DateTimeOffset.UtcNow.AddDays(-100 - i)));

        var deleted = await harness.DAL.PurgeAsync(CancellationToken.None);

        Assert.AreEqual(12, deleted);
        using var ctx = harness.NewContext();
        Assert.AreEqual(0, ctx.KafkaErrorLogEntries.Count());
    }

    private static KafkaErrorLogEntry NewEntry(byte[] payload) => new()
    {
        Topic = "t",
        Partition = 0,
        Offset = 1,
        ConsumerGroup = "g",
        Error = "boom",
        OccurredAt = DateTimeOffset.UtcNow,
        RawPayload = payload
    };

    private static async Task Seed(InMemoryTestHarness harness, (string Topic, string Group, DateTimeOffset At) row)
    {
        await harness.DAL.InsertAsync(new KafkaErrorLogEntry
        {
            Topic = row.Topic,
            Partition = 0,
            Offset = 1,
            ConsumerGroup = row.Group,
            Error = "x",
            OccurredAt = row.At
        }, CancellationToken.None);
    }
}

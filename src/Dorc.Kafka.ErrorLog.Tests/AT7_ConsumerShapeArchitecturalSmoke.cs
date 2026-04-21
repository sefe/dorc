using System.Text;
using Confluent.Kafka;
using Dorc.PersistentData.Model;

namespace Dorc.Kafka.ErrorLog.Tests;

/// <summary>
/// AT-7 (architectural smoke per SPEC-S-004 §4): the DAL surface is
/// reachable from an S-002-style consumer call site without S-006 having
/// to retrofit either side. Success signal is twofold: this project
/// compiles AND the named test below executes the construction +
/// InsertAsync call against the real DAL with a real ConsumeResult-derived
/// payload. Behavioural proof of the R-7 handshake (offset-commit
/// ordering, fallback to structured log on DB failure) lives in S-006.
/// </summary>
[TestClass]
public class AT7_ConsumerShapeArchitecturalSmoke
{
    [TestMethod]
    public async Task ConsumeResultFailure_BuildsEntry_AndInsertsViaDAL()
    {
        using var harness = new InMemoryTestHarness();

        // Synthesise a ConsumeResult shape an S-002 consumer would hand us
        // when value-deserialisation throws.
        var consumeResult = new ConsumeResult<string, byte[]>
        {
            Topic = "dorc.requests.new",
            Partition = new Partition(3),
            Offset = new Offset(2025),
            Message = new Message<string, byte[]>
            {
                Key = "deployment-42",
                Value = Encoding.UTF8.GetBytes("malformed-avro-payload"),
                Timestamp = new Timestamp(DateTimeOffset.UtcNow)
            }
        };
        Exception failure;
        try
        {
            throw new InvalidOperationException("Avro decode failed: missing required field 'Status'");
        }
        catch (InvalidOperationException ex)
        {
            failure = ex;
        }

        var entry = BuildEntryFromConsumeFailure(consumeResult, "dorc-monitor", failure);

        await harness.DAL.InsertAsync(entry, CancellationToken.None);

        using var ctx = harness.NewContext();
        var stored = ctx.KafkaErrorLogEntries.Single();
        Assert.AreEqual("dorc.requests.new", stored.Topic);
        Assert.AreEqual(3, stored.Partition);
        Assert.AreEqual(2025L, stored.Offset);
        Assert.AreEqual("dorc-monitor", stored.ConsumerGroup);
        Assert.AreEqual("deployment-42", stored.MessageKey);
        CollectionAssert.AreEqual(consumeResult.Message.Value, stored.RawPayload);
        StringAssert.Contains(stored.Error, "Avro decode failed");
        Assert.IsNotNull(stored.Stack);
    }

    /// <summary>
    /// Reference call-site shape for S-006/S-007/S-008 wiring. Anchors the
    /// caller-vs-DAL field split documented in SPEC §2 R-4. Drift here
    /// fails the test build, surfacing the contract change before S-006
    /// has to discover it.
    /// </summary>
    internal static KafkaErrorLogEntry BuildEntryFromConsumeFailure(
        ConsumeResult<string, byte[]> result,
        string consumerGroup,
        Exception failure)
    {
        return new KafkaErrorLogEntry
        {
            Topic = result.Topic,
            Partition = result.Partition.Value,
            Offset = result.Offset.Value,
            ConsumerGroup = consumerGroup,
            MessageKey = result.Message.Key,
            RawPayload = result.Message.Value,
            Error = failure.Message,
            Stack = failure.StackTrace,
            OccurredAt = result.Message.Timestamp.UtcDateTime
        };
    }
}

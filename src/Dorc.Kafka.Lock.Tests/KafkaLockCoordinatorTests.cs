using System.Reflection;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock.Tests;

/// <summary>
/// In-process tests for the coordinator's partition-ownership state machine.
/// Does not touch Kafka — exercises the slot transitions directly via private
/// callbacks (AT-10) so the CTS/TCS semantics are unit-testable without a
/// broker. The HA suite (R-8) covers the librdkafka integration end-to-end.
/// </summary>
[TestClass]
public class KafkaLockCoordinatorTests
{
    private static KafkaLockCoordinator NewCoordinator(int partitionCount = 12)
    {
        var opts = Options.Create(new KafkaLocksOptions
        {
            Enabled = false, // disabled so no consumer is built in StartAsync
            PartitionCount = partitionCount,
            ConsumerGroupId = "test"
        });
        var topics = Options.Create(new KafkaTopicsOptions());
        return new KafkaLockCoordinator(
            new FakeConnectionProvider(),
            opts,
            topics,
            NullLogger<KafkaLockCoordinator>.Instance);
    }

    private static void InvokePrivate(object target, string name, params object[] args)
    {
        var m = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        m!.Invoke(target, args);
    }

    [TestMethod]
    public async Task Wait_CompletesWhenPartitionAssigned()
    {
        await using var c = NewCoordinator();
        var waitTask = c.WaitForPartitionOwnershipAsync(3, CancellationToken.None);
        Assert.IsFalse(waitTask.IsCompleted);
        InvokePrivate(c, "OnAssigned", 3);
        var token = await waitTask;
        Assert.IsFalse(token.IsCancellationRequested);
    }

    [TestMethod]
    public async Task Revoke_CancelsPriorToken_AndNewAcquireGetsFreshToken()
    {
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 5);
        var firstToken = await c.WaitForPartitionOwnershipAsync(5, CancellationToken.None);

        InvokePrivate(c, "OnRevokedOrLost", 5, false);
        Assert.IsTrue(firstToken.IsCancellationRequested, "Revoke must fire the prior LockLostToken.");

        var reacquire = c.WaitForPartitionOwnershipAsync(5, CancellationToken.None);
        Assert.IsFalse(reacquire.IsCompleted, "Fresh slot should block until reassigned.");
        InvokePrivate(c, "OnAssigned", 5);
        var secondToken = await reacquire;
        Assert.IsFalse(secondToken.IsCancellationRequested, "Fresh cycle must yield an uncancelled token.");
    }

    [TestMethod]
    public async Task Lost_FiresLockLostToken()
    {
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 7);
        var t = await c.WaitForPartitionOwnershipAsync(7, CancellationToken.None);
        InvokePrivate(c, "OnRevokedOrLost", 7, true);
        Assert.IsTrue(t.IsCancellationRequested);
    }

    [TestMethod]
    public async Task RepeatAssign_NoOpRebalance_DoesNotRecycleCts()
    {
        await using var c = NewCoordinator();
        InvokePrivate(c, "OnAssigned", 2);
        var first = await c.WaitForPartitionOwnershipAsync(2, CancellationToken.None);
        InvokePrivate(c, "OnAssigned", 2); // duplicate
        var second = await c.WaitForPartitionOwnershipAsync(2, CancellationToken.None);
        Assert.AreEqual(first, second, "No-op reassignment must preserve the live LockLostToken.");
    }

    [TestMethod]
    public async Task Wait_Cancellation_ThrowsOperationCanceled()
    {
        await using var c = NewCoordinator();
        using var cts = new CancellationTokenSource();
        var task = c.WaitForPartitionOwnershipAsync(11, cts.Token);
        cts.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await task);
    }

    [TestMethod]
    public async Task GetPartitionFor_IsStable()
    {
        await using var c = NewCoordinator();
        var p1 = c.GetPartitionFor("env:Production");
        var p2 = c.GetPartitionFor("env:Production");
        Assert.AreEqual(p1, p2);
        Assert.IsTrue(p1 >= 0 && p1 < 12);
    }

    private sealed class FakeConnectionProvider : Dorc.Kafka.Client.Connection.IKafkaConnectionProvider
    {
        public Confluent.Kafka.ProducerConfig GetProducerConfig() => new();
        public Confluent.Kafka.ConsumerConfig GetConsumerConfig(string? groupIdOverride = null) => new() { GroupId = groupIdOverride ?? "test" };
    }
}

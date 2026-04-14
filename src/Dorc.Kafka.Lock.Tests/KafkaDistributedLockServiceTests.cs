using System.Reflection;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock.Tests;

/// <summary>
/// AT-1 (acquire/release), AT-10 (LockLostToken on both shapes), and the
/// wait-cap null-return path (R-2 ADR-deviation shim).
/// </summary>
[TestClass]
public class KafkaDistributedLockServiceTests
{
    private static (KafkaLockCoordinator coordinator, KafkaDistributedLockService service) Build(
        bool enabled = true, int waitDefaultMs = 30_000)
    {
        var opts = Options.Create(new KafkaLocksOptions
        {
            Enabled = enabled,
            PartitionCount = 12,
            ConsumerGroupId = "test",
            LockWaitDefaultTimeoutMs = waitDefaultMs
        });
        var coordinator = new KafkaLockCoordinator(
            new FakeConnectionProvider(), opts, NullLogger<KafkaLockCoordinator>.Instance);
        var service = new KafkaDistributedLockService(
            coordinator, opts, NullLogger<KafkaDistributedLockService>.Instance);
        return (coordinator, service);
    }

    private static void InvokePrivate(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, args);

    [TestMethod]
    public async Task Acquire_OnOwnedPartition_ReturnsValidLock()
    {
        var (coord, svc) = Build();
        await using var _ = coord;

        // Pre-assign all partitions so any resource key resolves immediately.
        for (var p = 0; p < coord.Options.PartitionCount; p++)
            InvokePrivate(coord, "OnAssigned", p);

        var lockHandle = await svc.TryAcquireLockAsync("env:Production", 5_000, CancellationToken.None);
        Assert.IsNotNull(lockHandle);
        Assert.IsTrue(lockHandle!.IsValid);
        Assert.AreEqual("env:Production", lockHandle.ResourceKey);
        Assert.IsFalse(lockHandle.LockLostToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task Acquire_TimesOut_ReturnsNull()
    {
        var (coord, svc) = Build(waitDefaultMs: 50);
        await using var _ = coord;
        // No partitions assigned — the wait must time out and yield null.
        var lockHandle = await svc.TryAcquireLockAsync("env:Foo", leaseTimeMs: 50, CancellationToken.None);
        Assert.IsNull(lockHandle);
    }

    [TestMethod]
    public async Task Acquire_CallerCancelled_ReturnsNull()
    {
        var (coord, svc) = Build();
        await using var _ = coord;
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);
        var lockHandle = await svc.TryAcquireLockAsync("env:Foo", leaseTimeMs: 10_000, cts.Token);
        Assert.IsNull(lockHandle);
    }

    [TestMethod]
    public async Task Disabled_ReturnsNull()
    {
        var (coord, svc) = Build(enabled: false);
        await using var _ = coord;
        Assert.IsFalse(svc.IsEnabled);
        var lockHandle = await svc.TryAcquireLockAsync("env:Foo", 5_000, CancellationToken.None);
        Assert.IsNull(lockHandle);
    }

    [TestMethod]
    public async Task LockLostToken_FiresOnRevoke()
    {
        var (coord, svc) = Build();
        await using var _ = coord;
        for (var p = 0; p < coord.Options.PartitionCount; p++)
            InvokePrivate(coord, "OnAssigned", p);

        var lockHandle = await svc.TryAcquireLockAsync("env:Production", 5_000, CancellationToken.None);
        Assert.IsNotNull(lockHandle);

        // Revoke the owning partition — the held lock's token must fire.
        InvokePrivate(coord, "OnRevokedOrLost", ((KafkaDistributedLock)lockHandle!).Partition, false);
        Assert.IsTrue(lockHandle.LockLostToken.IsCancellationRequested);
        Assert.IsFalse(lockHandle.IsValid);
    }

    [TestMethod]
    public async Task LockLostToken_FiresOnLost()
    {
        var (coord, svc) = Build();
        await using var _ = coord;
        for (var p = 0; p < coord.Options.PartitionCount; p++)
            InvokePrivate(coord, "OnAssigned", p);

        var lockHandle = await svc.TryAcquireLockAsync("env:Production", 5_000, CancellationToken.None);
        InvokePrivate(coord, "OnRevokedOrLost", ((KafkaDistributedLock)lockHandle!).Partition, true);
        Assert.IsTrue(lockHandle.LockLostToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task DistinctKeys_AcquireIndependently()
    {
        var (coord, svc) = Build();
        await using var _ = coord;
        for (var p = 0; p < coord.Options.PartitionCount; p++)
            InvokePrivate(coord, "OnAssigned", p);

        var a = await svc.TryAcquireLockAsync("env:A", 5_000, CancellationToken.None);
        var b = await svc.TryAcquireLockAsync("env:B", 5_000, CancellationToken.None);
        Assert.IsNotNull(a); Assert.IsNotNull(b);
    }

    private sealed class FakeConnectionProvider : Dorc.Kafka.Client.Connection.IKafkaConnectionProvider
    {
        public Confluent.Kafka.ProducerConfig GetProducerConfig() => new();
        public Confluent.Kafka.ConsumerConfig GetConsumerConfig(string? groupIdOverride = null) => new() { GroupId = groupIdOverride ?? "test" };
    }
}

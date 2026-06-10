using System.Collections.Concurrent;
using System.Reflection;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock.Tests;

/// <summary>
/// (acquire/release),  (LockLostToken on both shapes), the
/// AcquireWaitMs wait-cap (leaseTimeMs is ignored — partition ownership has no
/// lease), and revoke-while-waiting re-entry.
/// </summary>
[TestClass]
public class KafkaDistributedLockServiceTests
{
    private static (KafkaLockCoordinator coordinator, KafkaDistributedLockService service) Build(
        bool enabled = true, int acquireWaitMs = 5_000, ILogger<KafkaDistributedLockService>? logger = null)
    {
        var opts = Options.Create(new KafkaLocksOptions
        {
            Enabled = enabled,
            PartitionCount = 12,
            ConsumerGroupId = "test",
            AcquireWaitMs = acquireWaitMs
        });
        var topics = Options.Create(new KafkaTopicsOptions());
        var coordinator = new KafkaLockCoordinator(
            new FakeConnectionProvider(), opts, topics, NullLogger<KafkaLockCoordinator>.Instance);
        var service = new KafkaDistributedLockService(
            coordinator, opts, logger ?? NullLogger<KafkaDistributedLockService>.Instance);
        return (coordinator, service);
    }

    private static void InvokePrivate(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, args);

    private static bool WaitForCancellation(CancellationToken token, TimeSpan? timeout = null)
    {
        using var fired = new ManualResetEventSlim();
        using var reg = token.Register(fired.Set);
        return fired.Wait(timeout ?? TimeSpan.FromSeconds(10));
    }

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
        var (coord, svc) = Build(acquireWaitMs: 50);
        await using var _ = coord;
        // No partitions assigned — the wait must time out and yield null.
        var lockHandle = await svc.TryAcquireLockAsync("env:Foo", leaseTimeMs: 300_000, CancellationToken.None);
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
        // (Cancellation is dispatched off the revoking thread, so wait for it.)
        InvokePrivate(coord, "OnRevokedOrLost", ((KafkaDistributedLock)lockHandle!).Partition, false);
        Assert.IsTrue(WaitForCancellation(lockHandle.LockLostToken));
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
        Assert.IsTrue(WaitForCancellation(lockHandle.LockLostToken));
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

    // ---- Finding 5: leaseTimeMs must NOT be reinterpreted as the wait cap ----

    [TestMethod]
    public async Task Acquire_IgnoresLeaseTime_WaitIsCappedByAcquireWaitMs()
    {
        // Monitor passes leaseTimeMs = 300_000 (a lease duration). The old code
        // used it as the wait cap, parking a task for 5 minutes per poll cycle
        // on a contested environment.
        var (coord, svc) = Build(acquireWaitMs: 200);
        await using var _ = coord;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lockHandle = await svc.TryAcquireLockAsync("env:Contested", leaseTimeMs: 300_000, CancellationToken.None);
        sw.Stop();

        Assert.IsNull(lockHandle);
        Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(30),
            $"Wait must be capped by AcquireWaitMs (200ms), not leaseTimeMs (300s); waited {sw.Elapsed}.");
    }

    // ---- Finding 4: revoke during the wait must re-enter, not return null ----

    [TestMethod]
    public async Task Acquire_RevokedWhileWaiting_ReentersAndAcquiresFreshSlot()
    {
        var (coord, svc) = Build(acquireWaitMs: 30_000);
        await using var _ = coord;

        var partition = coord.GetPartitionFor("env:Production");
        var acquireTask = svc.TryAcquireLockAsync("env:Production", 0, CancellationToken.None);

        // Wait until the acquire call has registered a waiter (slot exists).
        var slotsField = typeof(KafkaLockCoordinator)
            .GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var slots = (ConcurrentDictionary<int, KafkaLockCoordinator.PartitionSlot>)slotsField.GetValue(coord)!;
        Assert.IsTrue(SpinWait.SpinUntil(() => slots.ContainsKey(partition), TimeSpan.FromSeconds(10)));

        // Revoke while the waiter is parked: the old code surfaced this as a
        // null return mislabelled "wait-timeout"; the fix re-enters the wait.
        InvokePrivate(coord, "OnRevokedOrLost", partition, false);
        await Task.Delay(250); // let the (async) slot cancellation propagate
        Assert.IsFalse(acquireTask.IsCompleted,
            "A revoke during the wait must re-enter against the fresh slot, not return null.");

        InvokePrivate(coord, "OnAssigned", partition);
        var lockHandle = await acquireTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsNotNull(lockHandle, "Re-entered wait must succeed once the partition is assigned.");
        Assert.IsTrue(lockHandle!.IsValid);
    }

    // ---- Finding 4: outcomes must be labelled correctly ----

    [TestMethod]
    public async Task Acquire_CallerCancelled_LogsCallerCancelledOutcome()
    {
        var logger = new CapturingLogger();
        var (coord, svc) = Build(acquireWaitMs: 30_000, logger: logger);
        await using var _ = coord;

        using var cts = new CancellationTokenSource(50);
        var lockHandle = await svc.TryAcquireLockAsync("env:Foo", 0, cts.Token);

        Assert.IsNull(lockHandle);
        Assert.IsTrue(logger.Messages.Any(m => m.Contains("outcome=caller-cancelled")),
            $"Expected caller-cancelled outcome; got: {string.Join(" | ", logger.Messages)}");
    }

    [TestMethod]
    public async Task Acquire_WaitCapExpires_LogsWaitTimeoutOutcome()
    {
        var logger = new CapturingLogger();
        var (coord, svc) = Build(acquireWaitMs: 50, logger: logger);
        await using var _ = coord;

        var lockHandle = await svc.TryAcquireLockAsync("env:Foo", 0, CancellationToken.None);

        Assert.IsNull(lockHandle);
        Assert.IsTrue(logger.Messages.Any(m => m.Contains("outcome=wait-timeout")),
            $"Expected wait-timeout outcome; got: {string.Join(" | ", logger.Messages)}");
    }

    private sealed class CapturingLogger : ILogger<KafkaDistributedLockService>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Enqueue(formatter(state, exception));
    }

    private sealed class FakeConnectionProvider : Dorc.Kafka.Client.Connection.IKafkaConnectionProvider
    {
        public Confluent.Kafka.ProducerConfig GetProducerConfig() => new();
        public Confluent.Kafka.ConsumerConfig GetConsumerConfig(string? groupIdOverride = null) => new() { GroupId = groupIdOverride ?? "test" };
    }
}

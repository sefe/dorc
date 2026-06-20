using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Observability;
using Dorc.Kafka.Client.Serialization;
using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// Pins the audit F1/F2 fixes on <see cref="DeploymentResultsKafkaConsumer"/>:
/// <list type="bullet">
///   <item>F1 — a transient broadcast failure is retried (bounded) before the
///   record is routed to the error-log + commit path. results.status has no DLQ,
///   so without retry a single transient SignalR hiccup would drop a real-time
///   UI event forever.</item>
///   <item>F2 — each broadcast attempt is bounded by a timeout so a stalled
///   broadcaster can't pin the poll thread past max.poll.interval.ms and fence
///   the consumer.</item>
/// </list>
/// The retry/timeout logic is exercised directly via the internal
/// <c>TryBroadcast</c> seam (mirrors how <c>BuildConsumerConfig</c> is tested),
/// so no broker is required.
/// </summary>
[TestClass]
public class BroadcastRetryTests
{
    private static readonly DeploymentResultEventData Event = new();

    [TestMethod]
    public void TryBroadcast_SucceedsFirstAttempt_ReturnsDelivered_NoRetry()
    {
        var broadcaster = new ConfigurableBroadcaster((_, _) => Task.CompletedTask);
        var sut = NewConsumer(broadcaster, maxAttempts: 3);

        var outcome = sut.TryBroadcast(Event, CancellationToken.None, out var failure);

        Assert.AreEqual(DeploymentResultsKafkaConsumer.BroadcastOutcome.Delivered, outcome);
        Assert.IsNull(failure);
        Assert.AreEqual(1, broadcaster.CallCount);
    }

    [TestMethod]
    public void TryBroadcast_TransientFailuresThenSuccess_ReturnsDelivered()
    {
        // Fail the first two attempts, succeed on the third — the F1 case.
        var failures = 2;
        var broadcaster = new ConfigurableBroadcaster((_, _) =>
        {
            if (Volatile.Read(ref failures) > 0)
            {
                Interlocked.Decrement(ref failures);
                throw new InvalidOperationException("transient signalr hiccup");
            }
            return Task.CompletedTask;
        });
        var sut = NewConsumer(broadcaster, maxAttempts: 3);

        var outcome = sut.TryBroadcast(Event, CancellationToken.None, out var failure);

        Assert.AreEqual(DeploymentResultsKafkaConsumer.BroadcastOutcome.Delivered, outcome);
        Assert.IsNull(failure);
        Assert.AreEqual(3, broadcaster.CallCount);
    }

    [TestMethod]
    public void TryBroadcast_AllAttemptsFail_ReturnsFailed_WithLastException()
    {
        var broadcaster = new ConfigurableBroadcaster(
            (_, _) => throw new InvalidOperationException("permanent failure"));
        var sut = NewConsumer(broadcaster, maxAttempts: 3);

        var outcome = sut.TryBroadcast(Event, CancellationToken.None, out var failure);

        Assert.AreEqual(DeploymentResultsKafkaConsumer.BroadcastOutcome.Failed, outcome);
        Assert.IsInstanceOfType(failure, typeof(InvalidOperationException));
        Assert.AreEqual(3, broadcaster.CallCount,
            "Every attempt up to MaxBroadcastAttempts must be tried before giving up.");
    }

    [TestMethod]
    public void TryBroadcast_StoppingTokenAlreadyCancelled_ReturnsShuttingDown_WithoutBroadcasting()
    {
        var broadcaster = new ConfigurableBroadcaster((_, _) => Task.CompletedTask);
        var sut = NewConsumer(broadcaster, maxAttempts: 3);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = sut.TryBroadcast(Event, cts.Token, out var failure);

        Assert.AreEqual(DeploymentResultsKafkaConsumer.BroadcastOutcome.ShuttingDown, outcome);
        Assert.IsNull(failure);
        Assert.AreEqual(0, broadcaster.CallCount);
    }

    [TestMethod]
    public void TryBroadcast_StalledBroadcaster_TimesOutAttempt_AndFails()
    {
        // F2: the broadcaster blocks until its token is cancelled. With a tiny
        // per-attempt timeout and a single attempt, TryBroadcast must bound the
        // wait and report Failed rather than block the poll thread forever.
        var broadcaster = new ConfigurableBroadcaster(
            (_, ct) => Task.Delay(Timeout.Infinite, ct));
        var sut = NewConsumer(broadcaster, maxAttempts: 1, timeoutMs: 50);

        var outcome = sut.TryBroadcast(Event, CancellationToken.None, out var failure);

        Assert.AreEqual(DeploymentResultsKafkaConsumer.BroadcastOutcome.Failed, outcome);
        Assert.IsInstanceOfType(failure, typeof(TimeoutException),
            "A timed-out attempt must surface as a TimeoutException (accurate reason for the error log), not a generic cancellation (audit round-2 #4).");
        Assert.AreEqual(1, broadcaster.CallCount);
    }

    [TestMethod]
    public void TryBroadcast_BroadcasterIgnoresToken_StillTimesOut()
    {
        // Audit round-2 #1: the production SignalR broadcaster does NOT observe
        // the cancellation token. The per-attempt timeout must still release the
        // poll thread (enforced via Task.WaitAsync), so a token-ignoring stalled
        // broadcaster cannot fence the consumer.
        var broadcaster = new ConfigurableBroadcaster(
            (_, _) => new TaskCompletionSource<bool>().Task); // never completes, ignores token
        var sut = NewConsumer(broadcaster, maxAttempts: 1, timeoutMs: 50);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var outcome = sut.TryBroadcast(Event, CancellationToken.None, out var failure);
        sw.Stop();

        Assert.AreEqual(DeploymentResultsKafkaConsumer.BroadcastOutcome.Failed, outcome);
        Assert.IsInstanceOfType(failure, typeof(TimeoutException));
        Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"Timeout must fire regardless of token cooperation; waited {sw.Elapsed}.");
    }

    private static DeploymentResultsKafkaConsumer NewConsumer(
        IDeploymentResultBroadcaster broadcaster,
        int maxAttempts = 3,
        int timeoutMs = 10_000,
        int retryDelayMs = 0)
        => new DeploymentResultsKafkaConsumer(
            new ThrowingConnectionProvider(),
            new DefaultKafkaSerializerFactory(),
            broadcaster,
            new NoopErrorLog(),
            Options.Create(new KafkaErrorLogOptions()),
            Options.Create(new KafkaTopicsOptions()),
            new NoOpKafkaConsumerMetrics(),
            NullLogger<DeploymentResultsKafkaConsumer>.Instance)
        {
            MaxBroadcastAttempts = maxAttempts,
            BroadcastTimeoutMs = timeoutMs,
            BroadcastRetryDelayMs = retryDelayMs
        };

    private sealed class ConfigurableBroadcaster : IDeploymentResultBroadcaster
    {
        private readonly Func<DeploymentResultEventData, CancellationToken, Task> _behavior;
        private int _callCount;

        public ConfigurableBroadcaster(Func<DeploymentResultEventData, CancellationToken, Task> behavior)
            => _behavior = behavior;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task BroadcastAsync(DeploymentResultEventData eventData, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return _behavior(eventData, cancellationToken);
        }
    }

    /// <summary>TryBroadcast never touches the connection provider; fail loudly if it does.</summary>
    private sealed class ThrowingConnectionProvider : IKafkaConnectionProvider
    {
        public ConsumerConfig GetConsumerConfig(string? groupIdOverride = null) => throw new NotSupportedException();
        public ProducerConfig GetProducerConfig() => throw new NotSupportedException();
    }

    private sealed class NoopErrorLog : IKafkaErrorLog
    {
        public Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

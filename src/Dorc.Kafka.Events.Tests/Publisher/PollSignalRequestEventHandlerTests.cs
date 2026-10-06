using Dorc.Core.Events;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// The handler raises the wake-up signal exactly once per requests.new
/// record, and for requests.status records ONLY when the status is a
/// user-action state the Monitor's sweeps act on (Cancelling/Restarting/
/// Pending). The high-frequency Monitor-published result states (Running/
/// Completed/Failed/Errored/...) stay filtered: signalling on them made every
/// processed request wake the poll loop again (self-wake; each wake costs a
/// forced full GC + 4 DB sweeps in DeploymentEngine).
/// </summary>
[TestClass]
public class PollSignalRequestEventHandlerTests
{
    private static readonly KafkaTopicsOptions Topics = new();

    private static PollSignalRequestEventHandler NewHandler(IRequestPollSignal signal, KafkaTopicsOptions? topics = null)
        => new(signal, Options.Create(topics ?? new KafkaTopicsOptions()));

    [TestMethod]
    public async Task Handle_RequestsNewRecord_RaisesSignalOnce()
    {
        var signal = new CountingSignal();
        var sut = NewHandler(signal);

        await sut.HandleAsync(Topics.RequestsNew,
            new DeploymentRequestEventData(1, "Pending", null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.AreEqual(1, signal.SignalCount);
    }

    [DataTestMethod]
    [DataRow("Running")]
    [DataRow("Completed")]
    [DataRow("Failed")]
    [DataRow("Errored")]
    [DataRow("Cancelled")]
    public async Task Handle_RequestsStatusRecord_MonitorResultState_DoesNotSignal(string status)
    {
        // These are the Monitor's own high-frequency publishes — signalling
        // here would self-wake the poll loop on every processed request.
        var signal = new CountingSignal();
        var sut = NewHandler(signal);

        await sut.HandleAsync(Topics.RequestsStatus,
            new DeploymentRequestEventData(1, status, null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.AreEqual(0, signal.SignalCount);
    }

    [DataTestMethod]
    [DataRow("Cancelling")]
    [DataRow("Restarting")]
    [DataRow("Pending")]
    public async Task Handle_RequestsStatusRecord_UserActionState_Signals(string status)
    {
        // User cancel (Cancelling), restart (Restarting) and resume (Pending)
        // must wake the sweeps within one signal, not one poll interval.
        var signal = new CountingSignal();
        var sut = NewHandler(signal);

        await sut.HandleAsync(Topics.RequestsStatus,
            new DeploymentRequestEventData(1, status, null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.AreEqual(1, signal.SignalCount);
    }

    [TestMethod]
    public async Task Handle_RequestsStatusRecord_StatusMatchIsCaseInsensitive_AndNullStatusDoesNotSignal()
    {
        var signal = new CountingSignal();
        var sut = NewHandler(signal);

        await sut.HandleAsync(Topics.RequestsStatus,
            new DeploymentRequestEventData(1, "CANCELLING", null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.AreEqual(1, signal.SignalCount);

        await sut.HandleAsync(Topics.RequestsStatus,
            new DeploymentRequestEventData(2, null, null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.AreEqual(1, signal.SignalCount, "A null status must never signal.");
    }

    [TestMethod]
    public async Task Handle_FiltersOnConfiguredTopicName_NotTheDefault()
    {
        // SEFE-style enterprise naming: the filter must key off the
        // CONFIGURED RequestsNew name, not the dorc.* default.
        var signal = new CountingSignal();
        var topics = new KafkaTopicsOptions
        {
            RequestsNew = "tr.dv.gbl.deploy.request.il2.dorc",
            RequestsStatus = "tr.dv.gbl.deploy.requeststatus.il2.dorc"
        };
        var sut = NewHandler(signal, topics);
        var ev = new DeploymentRequestEventData(2, "Pending", null, null, DateTimeOffset.UtcNow);

        await sut.HandleAsync(topics.RequestsNew, ev, CancellationToken.None);
        await sut.HandleAsync("dorc.requests.new", ev, CancellationToken.None);

        Assert.AreEqual(1, signal.SignalCount);
    }

    [TestMethod]
    public async Task Handle_DuplicateRecords_RaiseSignalEachTime_ButPrimitiveCollapses()
    {
        // The handler signals every requests.new record. The downstream
        // SemaphoreSlim collapses to one pending wake; the handler itself
        // does not pre-collapse.
        var signal = new CountingSignal();
        var sut = NewHandler(signal);
        var ev = new DeploymentRequestEventData(7, "Pending", null, null, DateTimeOffset.UtcNow);

        for (var i = 0; i < 5; i++)
            await sut.HandleAsync(Topics.RequestsNew, ev, CancellationToken.None);

        Assert.AreEqual(5, signal.SignalCount);
    }

    private sealed class CountingSignal : IRequestPollSignal
    {
        public int SignalCount { get; private set; }
        public void Signal() => SignalCount++;
        public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

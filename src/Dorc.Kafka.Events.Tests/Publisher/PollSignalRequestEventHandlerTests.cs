using Dorc.Core.Events;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>
/// The handler raises the wake-up signal exactly once per requests.new
/// record — and ONLY for requests.new. requests.status records include the
/// Monitor's own status publishes, so signalling on them made every processed
/// request wake the poll loop again (self-wake; each wake costs a forced full
/// GC + 4 DB sweeps in DeploymentEngine).
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

    [TestMethod]
    public async Task Handle_RequestsStatusRecord_DoesNotSignal()
    {
        // requests.status events include the Monitor's own publishes —
        // signalling here would self-wake the poll loop on every processed
        // request.
        var signal = new CountingSignal();
        var sut = NewHandler(signal);

        await sut.HandleAsync(Topics.RequestsStatus,
            new DeploymentRequestEventData(1, "Running", null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.AreEqual(0, signal.SignalCount);
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

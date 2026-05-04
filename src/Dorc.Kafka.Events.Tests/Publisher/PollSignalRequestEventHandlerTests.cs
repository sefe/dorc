using Dorc.Core.Events;
using Dorc.Kafka.Events.Publisher;

namespace Dorc.Kafka.Events.Tests.Publisher;

/// <summary>AT-4 — handler raises the wake-up signal exactly once per record.</summary>
[TestClass]
public class PollSignalRequestEventHandlerTests
{
    [TestMethod]
    public async Task Handle_RaisesSignalOnce()
    {
        var signal = new CountingSignal();
        var sut = new PollSignalRequestEventHandler(signal);

        await sut.HandleAsync("dorc.requests.new",
            new DeploymentRequestEventData(1, "Pending", null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.AreEqual(1, signal.SignalCount);
    }

    [TestMethod]
    public async Task Handle_DuplicateRecords_RaiseSignalEachTime_ButPrimitiveCollapses()
    {
        // Per R-4 / AT-4, the handler signals every record. The downstream
        // SemaphoreSlim collapses to one pending wake; the handler itself
        // does not pre-collapse.
        var signal = new CountingSignal();
        var sut = new PollSignalRequestEventHandler(signal);
        var ev = new DeploymentRequestEventData(7, "Cancelled", null, null, DateTimeOffset.UtcNow);

        for (var i = 0; i < 5; i++)
            await sut.HandleAsync("dorc.requests.status", ev, CancellationToken.None);

        Assert.AreEqual(5, signal.SignalCount);
    }

    private sealed class CountingSignal : IRequestPollSignal
    {
        public int SignalCount { get; private set; }
        public void Signal() => SignalCount++;
        public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

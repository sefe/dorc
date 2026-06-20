using Dorc.Core.Events;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// handler hook. Receives a <see cref="DeploymentRequestEventData"/>
/// pulled from <c>dorc.requests.new</c> or <c>dorc.requests.status</c> and
/// performs the acceleration-only side effect (raising the poll signal in
/// production). The consumer never executes requests through this handler;
/// it only signals the Monitor poll loop to wake up sooner.
///
/// <para><b>Idempotency invariant (audit F4):</b> implementations MUST be
/// idempotent. <see cref="DeploymentRequestsKafkaConsumer"/> consumes
/// at-least-once — it stages offsets via <c>StoreOffset</c> after the handler
/// succeeds and relies on the auto-commit timer to flush them, so a crash in
/// the window between handler success and the next commit will redeliver the
/// record on restart. The production
/// <see cref="PollSignalRequestEventHandler"/> satisfies this (raising the
/// poll signal twice is harmless); any future stateful handler (dedup state,
/// metrics, side-effecting writes) must tolerate duplicate delivery.</para>
/// </summary>
public interface IRequestEventHandler
{
    Task HandleAsync(string topic, DeploymentRequestEventData eventData, CancellationToken cancellationToken);
}

public sealed class PollSignalRequestEventHandler : IRequestEventHandler
{
    private readonly IRequestPollSignal _signal;
    public PollSignalRequestEventHandler(IRequestPollSignal signal) => _signal = signal;

    public Task HandleAsync(string topic, DeploymentRequestEventData eventData, CancellationToken cancellationToken)
    {
        _signal.Signal();
        return Task.CompletedTask;
    }
}

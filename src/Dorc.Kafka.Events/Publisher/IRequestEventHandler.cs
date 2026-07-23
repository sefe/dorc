using Dorc.ApiModel;
using Dorc.Core.Events;
using Dorc.Kafka.Events.Configuration;
using Microsoft.Extensions.Options;

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
    /// <summary>
    /// requests.status states that wake the poll loop. These are the
    /// USER-ACTION states the Monitor's sweeps act on: the API publishes
    /// Cancelling/Restarting (user cancel/restart of an in-flight request)
    /// and Pending (resume of a paused request). The Monitor never publishes
    /// Cancelling/Restarting itself, so those can't self-wake. It DOES
    /// publish Pending (startup resume in CancelStaleRequests, and
    /// RestartRequests processing) — accepted: those are low-frequency (one
    /// extra sweep per startup/restart, not per-deployment-burst) and Pending
    /// is genuinely actionable (runnable work exists). The high-frequency
    /// Monitor-published result states (Running/Completed/Failed/Errored/
    /// Cancelled/Abandoned) stay filtered — signalling on them made every
    /// processed request wake the loop again (self-wake), and each wake costs
    /// a forced full GC plus four DB sweeps in DeploymentEngine.
    /// </summary>
    private static readonly HashSet<string> WakeStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(DeploymentRequestStatus.Cancelling),
        nameof(DeploymentRequestStatus.Restarting),
        nameof(DeploymentRequestStatus.Pending),
    };

    private readonly IRequestPollSignal _signal;
    private readonly string _requestsNewTopic;
    private readonly string _requestsStatusTopic;

    public PollSignalRequestEventHandler(IRequestPollSignal signal, IOptions<KafkaTopicsOptions> topics)
    {
        _signal = signal;
        _requestsNewTopic = topics.Value.RequestsNew;
        _requestsStatusTopic = topics.Value.RequestsStatus;
    }

    public Task HandleAsync(string topic, DeploymentRequestEventData eventData, CancellationToken cancellationToken)
    {
        // requests.new events always wake the poll loop: they are the "a new
        // request needs picking up" signal the acceleration path exists for.
        // requests.status events wake it only for user-action states (see
        // WakeStatuses) so cancel/restart/resume are picked up within one
        // signal rather than one poll interval.
        if (string.Equals(topic, _requestsNewTopic, StringComparison.Ordinal))
            _signal.Signal();
        else if (string.Equals(topic, _requestsStatusTopic, StringComparison.Ordinal)
                 && eventData.Status is { } status && WakeStatuses.Contains(status))
            _signal.Signal();
        return Task.CompletedTask;
    }
}

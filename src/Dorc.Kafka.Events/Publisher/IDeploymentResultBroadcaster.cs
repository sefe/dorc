using Dorc.Core.Events;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Substitution point for the SignalR-projection step in the
/// Kafka → SignalR pipeline. Production wiring (Dorc.Api) implements this
/// over <c>IHubContext&lt;DeploymentsHub, IDeploymentsEventsClient&gt;</c>;
/// tests substitute a recording fake. Keeps Dorc.Kafka.Events free of any
/// SignalR / ASP.NET dependency.
///
/// <para><b>Idempotency invariant (audit round-2 #7):</b> implementations should
/// tolerate the same <see cref="DeploymentResultEventData"/> being broadcast more
/// than once. <see cref="DeploymentResultsKafkaConsumer"/> retries a failed
/// broadcast up to a bounded number of attempts, and a rebalance can redeliver a
/// record, so a partially-delivered-then-failed send may be re-broadcast. Status
/// events are last-writer-wins (the UI renders the latest state), so a duplicate
/// is benign; implementations must not perform non-idempotent side effects per
/// call.</para>
/// </summary>
public interface IDeploymentResultBroadcaster
{
    Task BroadcastAsync(DeploymentResultEventData eventData, CancellationToken cancellationToken);
}

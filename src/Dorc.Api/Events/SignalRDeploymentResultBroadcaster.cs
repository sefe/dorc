using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Kafka.Events.Publisher;
using Microsoft.AspNetCore.SignalR;

namespace Dorc.Api.Events
{
    /// <summary>
    /// Bridges the  <see cref="IDeploymentResultBroadcaster"/> contract
    /// onto the existing <see cref="DeploymentsHub"/> / SignalR pipeline.
    /// The DeploymentResultsKafkaConsumer (running in each API replica)
    /// calls <see cref="BroadcastAsync"/> for each Kafka message it
    /// consumes; this forwards to the hub's per-group broadcast via the
    /// existing group-tracker so the web UI sees the event identically to
    /// the direct-SignalR path.
    ///
    /// Exactly-once delivery depends on the consumer-group mode matching the
    /// SignalR topology (decided in AddDorcKafkaResultsStatusSubstrate):
    /// <list type="bullet">
    /// <item><description><b>In-process SignalR</b> (per-replica consumer
    /// groups): every replica receives every event; a hub send reaches only
    /// the clients pinned to THIS replica's hub instance — exactly once per
    /// client.</description></item>
    /// <item><description><b>Azure SignalR Service</b> (shared/competing
    /// group): a hub send is delivered service-wide to ALL clients, so
    /// exactly one replica may consume each event — the shared group
    /// guarantees that. Per-replica groups in this mode would deliver every
    /// event N× per client.</description></item>
    /// </list>
    /// </summary>
    public sealed class SignalRDeploymentResultBroadcaster : IDeploymentResultBroadcaster
    {
        private readonly IHubContext<DeploymentsHub, IDeploymentsEventsClient> _hub;
        private readonly IDeploymentSubscriptionsGroupTracker _tracker;

        public SignalRDeploymentResultBroadcaster(
            IHubContext<DeploymentsHub, IDeploymentsEventsClient> hub,
            IDeploymentSubscriptionsGroupTracker tracker)
        {
            _hub = hub;
            _tracker = tracker;
        }

        public async Task BroadcastAsync(DeploymentResultEventData eventData, CancellationToken cancellationToken)
        {
            // Mirror DirectDeploymentEventPublisher.PublishResultStatusChangedAsync:
            // dispatch to the per-RequestId subscription group so only subscribed
            // clients on this replica's hub instance receive the event.
            await _hub.Clients
                .Group(_tracker.GetGroupName(eventData.RequestId))
                .OnDeploymentResultStatusChanged(eventData);
        }
    }
}

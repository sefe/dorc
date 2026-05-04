using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Kafka.Events.Publisher;
using Microsoft.AspNetCore.SignalR;

namespace Dorc.Api.Events
{
    /// <summary>
    /// Bridges the S-007 <see cref="IDeploymentResultBroadcaster"/> contract
    /// onto the existing <see cref="DeploymentsHub"/> / SignalR pipeline.
    /// The DeploymentResultsKafkaConsumer (running in each API replica)
    /// calls <see cref="BroadcastAsync"/> for each Kafka message it
    /// consumes; this forwards to the hub's per-group broadcast via the
    /// existing group-tracker so the web UI sees the event identically to
    /// the direct-SignalR path.
    ///
    /// Per SPEC-S-007 R-2 multi-replica fan-out: every API replica has its
    /// own Kafka consumer group (so all replicas receive all events) and
    /// this broadcaster fans out to whichever clients are pinned to THIS
    /// replica's SignalR hub instance. Combined with the per-replica
    /// group.id pattern, each connected client receives each event exactly
    /// once.
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

using Dorc.Core.Events;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Substitution point for the SignalR-projection step in the
/// Kafka → SignalR pipeline. Production wiring (Dorc.Api) implements this
/// over <c>IHubContext&lt;DeploymentsHub, IDeploymentsEventsClient&gt;</c>;
/// tests substitute a recording fake. Keeps Dorc.Kafka.Events free of any
/// SignalR / ASP.NET dependency.
/// </summary>
public interface IDeploymentResultBroadcaster
{
    Task BroadcastAsync(DeploymentResultEventData eventData, CancellationToken cancellationToken);
}

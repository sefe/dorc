namespace Dorc.Core.Interfaces
{
    /// <summary>
    /// Marker shape over <see cref="IDeploymentEventsPublisher"/>. The
    /// Kafka-substrate publisher owns the result-status channel and delegates
    /// the request-lifecycle methods to an instance of this interface, which
    /// in production is bound to the direct-SignalR publisher. Retained
    /// post-S-009 so Dorc.Kafka.Events does not need to reference Dorc.Api.
    /// </summary>
    public interface IFallbackDeploymentEventPublisher : IDeploymentEventsPublisher
    {
    }
}

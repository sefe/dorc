namespace Dorc.Core.Interfaces
{
    /// <summary>
    /// Marker shape over <see cref="IDeploymentEventsPublisher"/> used during
    /// the S-006 / S-007 substrate transition. The Kafka-substrate publisher
    /// delegates the request-lifecycle methods (which S-007 does not own)
    /// to an instance of this interface — the production wiring binds it to
    /// the existing direct-SignalR publisher so request-lifecycle events keep
    /// flowing on SignalR until S-006 takes over those methods.
    ///
    /// Removed in S-009 alongside the substrate-selector flag and the
    /// inactive Direct branch.
    /// </summary>
    public interface IFallbackDeploymentEventPublisher : IDeploymentEventsPublisher
    {
    }
}

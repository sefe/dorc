using Dorc.Core.Events;
using Dorc.Core.Interfaces;

namespace Dorc.Api.Events
{
    /// <summary>
    /// Adapts the production <see cref="DirectDeploymentEventPublisher"/>
    /// onto the S-007 <see cref="IFallbackDeploymentEventPublisher"/> marker
    /// interface. Allows the Kafka-substrate publisher to delegate the
    /// two request-lifecycle methods (S-006's scope) to the existing
    /// SignalR-direct path without Dorc.Kafka.Events having to reference
    /// Dorc.Api.
    ///
    /// Removed in S-009 alongside the substrate-selector flag and the
    /// DirectDeploymentEventPublisher itself.
    /// </summary>
    public sealed class FallbackDeploymentEventPublisher : IFallbackDeploymentEventPublisher
    {
        private readonly DirectDeploymentEventPublisher _direct;

        public FallbackDeploymentEventPublisher(DirectDeploymentEventPublisher direct)
        {
            _direct = direct;
        }

        public Task PublishNewRequestAsync(DeploymentRequestEventData eventData)
            => _direct.PublishNewRequestAsync(eventData);

        public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData)
            => _direct.PublishRequestStatusChangedAsync(eventData);

        public Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData)
            => _direct.PublishResultStatusChangedAsync(eventData);
    }
}

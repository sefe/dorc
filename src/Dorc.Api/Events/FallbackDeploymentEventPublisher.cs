using Dorc.Core.Events;
using Dorc.Core.Interfaces;

namespace Dorc.Api.Events
{
    /// <summary>
    /// Adapts <see cref="DirectDeploymentEventPublisher"/> onto
    /// <see cref="IFallbackDeploymentEventPublisher"/> so the Kafka-substrate
    /// publisher can delegate the request-lifecycle methods to the SignalR
    /// path without Dorc.Kafka.Events having to reference Dorc.Api.
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

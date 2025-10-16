using Dorc.Core.Events;

namespace Dorc.Core.Interfaces
{
    public interface IDeploymentEventsPublisher
    {
        Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData);
        Task PublishNewRequestAsync(DeploymentRequestEventData eventData);
        Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData);
    }
}

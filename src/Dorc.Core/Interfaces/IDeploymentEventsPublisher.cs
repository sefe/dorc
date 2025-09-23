using Dorc.ApiModel;
using Dorc.Core.Events;

namespace Dorc.Core.Interfaces
{
    public interface IDeploymentEventsPublisher
    {
        Task PublishRequestStatusChangedAsync(DeploymentEventData eventData, CancellationToken ct = default);
        Task PublishNewRequestAsync(DeploymentEventData eventData, CancellationToken ct = default);
    }
}

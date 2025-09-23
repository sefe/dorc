using Dorc.ApiModel;
using Dorc.Core.Events;

namespace Dorc.Core.Interfaces
{
    public interface IDeploymentsEventsClient
    {
        Task OnDeploymentRequestStatusChanged(DeploymentEventData data);
        Task OnDeploymentRequestStarted(DeploymentEventData data);
        Task OnDeploymentResultStatusChanged(DeploymentEventData data);
    }
}

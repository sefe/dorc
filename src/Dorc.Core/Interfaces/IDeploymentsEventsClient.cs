using Dorc.ApiModel;
using Dorc.Core.Events;
using TypedSignalR.Client;

namespace Dorc.Core.Interfaces
{
    [Receiver]
    public interface IDeploymentsEventsClient
    {
        Task OnDeploymentRequestStatusChanged(DeploymentEventData data);
        Task OnDeploymentRequestStarted(DeploymentEventData data);
        Task OnDeploymentResultStatusChanged(DeploymentEventData data);
    }
}

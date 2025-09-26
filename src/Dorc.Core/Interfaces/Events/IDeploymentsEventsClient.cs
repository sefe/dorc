using Dorc.Core.Events;
using TypedSignalR.Client;

namespace Dorc.Core.Interfaces
{
    [Receiver]
    public interface IDeploymentsEventsClient
    {
        Task OnDeploymentRequestStatusChanged(DeploymentRequestEventData data);
        Task OnDeploymentRequestStarted(DeploymentRequestEventData data);
        Task OnDeploymentResultStatusChanged(DeploymentResultEventData data);
    }
}

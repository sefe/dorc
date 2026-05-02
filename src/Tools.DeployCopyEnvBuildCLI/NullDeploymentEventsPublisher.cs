using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using System.Threading.Tasks;

namespace Tools.DeployCopyEnvBuildCLI
{
    public class NullDeploymentEventsPublisher : IDeploymentEventsPublisher
    {
        public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData)
        {
            return Task.CompletedTask;
        }

        public Task PublishNewRequestAsync(DeploymentRequestEventData eventData)
        {
            return Task.CompletedTask;
        }

        public Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData)
        {
            return Task.CompletedTask;
        }
    }
}

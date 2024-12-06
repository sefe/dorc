using Dorc.ApiModel;
using Dorc.Core;

namespace Dorc.Monitor
{
    public class RequestToProcessDto
    {
        public RequestToProcessDto(DeploymentRequestApiModel request, DeploymentRequestDetail deploymentRequestDetail)
        {
            Request = request;
            Details = deploymentRequestDetail;
        }

        public DeploymentRequestApiModel Request { get; }
        public DeploymentRequestDetail Details { get; }
    }
}
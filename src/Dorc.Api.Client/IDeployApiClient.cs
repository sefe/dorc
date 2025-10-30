using Microsoft.Extensions.Logging;

namespace Dorc.Api.Client
{
    public interface IDeployApiClient
    {
        string urlParams { get; set; }

        void PostToDorc(ILogger<DeployApiClient> contextlogger, string endPoint, string urlParams);
        void PatchToDorc(ILogger<DeployApiClient> contextlogger, string endPoint, string patchContent);
    }
}
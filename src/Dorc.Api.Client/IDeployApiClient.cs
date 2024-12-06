using Serilog;

namespace Dorc.Api.Client
{
    public interface IDeployApiClient
    {
        string urlParams { get; set; }

        void PostToDorc(ILogger contextlogger, string endPoint, string urlParams);
        void PatchToDorc(ILogger contextlogger, string endPoint, string patchContent);
    }
}
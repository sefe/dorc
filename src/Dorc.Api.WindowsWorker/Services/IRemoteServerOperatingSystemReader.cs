using Dorc.ApiModel;

namespace Dorc.Api.WindowsWorker.Services
{
    public interface IRemoteServerOperatingSystemReader
    {
        ServerOperatingSystemApiModel? Read(string serverName);
    }
}

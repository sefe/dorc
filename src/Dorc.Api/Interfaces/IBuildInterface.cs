using Dorc.ApiModel;

namespace Dorc.Api.Interfaces
{
    public interface IDeployableBuildFactory
    {
        IDeployableBuild? CreateInstance(RequestDto request);
    }
}

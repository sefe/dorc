using Dorc.ApiModel;

namespace Dorc.Api.Windows.Interfaces
{
    public interface IDeployableBuildFactory
    {
        IDeployableBuild CreateInstance(RequestDto request);
    }
}

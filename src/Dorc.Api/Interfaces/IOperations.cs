using Dorc.ApiModel;
using System.Security.Claims;

namespace Dorc.Api.Interfaces
{
    public interface IApiServices
    {
        TemplateApiModel<ComponentApiModel> GetComponentsByProject(string projectName);
        EnvironmentContentApiModel GetEnvironmentsDetails(int id, ClaimsPrincipal user);

        EnvironmentApiModel ChangeEnvComponent<T>(int envId, int componentId, string action, string component,
            ClaimsPrincipal user);

        List<ServiceStatusApiModel> GetEnvDaemonsStatuses(int envId);
        List<ServiceStatusApiModel> GetEnvDaemonsStatuses(string envName, ClaimsPrincipal principal);
        ServiceStatusApiModel ChangeServiceState(ServiceStatusApiModel daemon, ClaimsPrincipal principal);
        List<ReleaseInformationApiModel> GetReleaseInformation(IEnumerable<int> requestIds);
    }
}
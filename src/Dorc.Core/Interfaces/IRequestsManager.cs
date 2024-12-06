using Dorc.ApiModel;
using Dorc.Core.Models;

namespace Dorc.Core.Interfaces
{
    public interface IRequestsManager
    {
        IEnumerable<DeployableComponent> GetComponents(int? projectId, int? parentId);
        IEnumerable<DeployableArtefact> GetBuildDefinitions(ProjectApiModel project);
        Task<IEnumerable<DeployableArtefact>> GetBuildsAsync(int? projectId, string environment, string buildDefinitionName);
        Task<List<DeploymentRequestDetail>> BundleRequestDetailAsync(CreateRequest createRequest);
        DeploymentRequestDetail RequestDetail(CreateRequest createRequest);
        IEnumerable<DeployableComponent> GetComponents(int? projectId);
    }
}
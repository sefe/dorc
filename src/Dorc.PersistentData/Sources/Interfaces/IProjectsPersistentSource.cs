using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using System.Security.Principal;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IProjectsPersistentSource
    {
        IEnumerable<ProjectApiModel> GetProjects(IPrincipal user, int deprecated = 0);
        ProjectApiModel GetProject(string projectName);
        ProjectApiModel? GetProject(int projectId);
        IEnumerable<EnvironmentApiModel> GetProjectEnvironments(int projectId, IPrincipal user);
        Project GetSecurityObject(string projectName);
        void ValidateProject(ProjectApiModel apiProject, HttpRequestType httpRequestType);
        void UpdateProject(ProjectApiModel newProjectDetails);
        void InsertProject(ProjectApiModel apiProject);
        IEnumerable<ComponentApiModel> GetComponentsForProject(string projectName);
        TemplateApiModel<EnvironmentApiModel>? GetProjectEnvironments(string projectName, IPrincipal user,
            bool includeRead);
        IEnumerable<ComponentApiModel> GetComponentsForProject(int projectId);
        bool RemoveEnvironmentMappingFromProject(string project, string environment, IPrincipal principal);
        bool AddEnvironmentMappingToProject(string project, string environment, IPrincipal principal);
        bool ProjectArtifactsUriFileValid(ProjectApiModel apiProject);
        bool ProjectArtifactsUriHttpValid(ProjectApiModel apiProject);
        Project GetSecurityObject(int projectId);
        void DeleteProject(int projectId);
    }
}
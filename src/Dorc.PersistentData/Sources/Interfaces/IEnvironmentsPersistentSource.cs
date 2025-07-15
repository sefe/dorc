using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using System.Security.Claims;
using System.Security.Principal;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IEnvironmentsPersistentSource
    {
        EnvironmentApiModel GetEnvironment(string environmentName, IPrincipal user);
        bool EnvironmentExists(string rowEnvironment);
        string GetEnvironmentOwnerId(int envId);
        bool SetEnvironmentOwner(IPrincipal updatedBy, int envId, UserElementApiModel user);
        EnvironmentApiModel AttachServerToEnv(int envId, int serverId, ClaimsPrincipal user);
        EnvironmentApiModel DetachServerFromEnv(int envId, int serverId, ClaimsPrincipal user);
        EnvironmentApiModel AttachDatabaseToEnv(int envId, int databaseId, ClaimsPrincipal user);
        EnvironmentApiModel DetachDatabaseFromEnv(int envId, int databaseId, ClaimsPrincipal user);
        Environment GetSecurityObject(string environmentName);
        EnvironmentApiModel CreateEnvironment(EnvironmentApiModel env, ClaimsPrincipal principal);
        IEnumerable<EnvironmentApiModel> GetEnvironments(IPrincipal user);
        EnvironmentApiModel GetEnvironment(int environmentId, ClaimsPrincipal user);
        IEnumerable<EnvironmentComponentStatusModel> GetEnvironmentComponentStatuses(int environmentId);
        IEnumerable<EnvironmentContentBuildsApiModel> GetEnvironmentComponentStatuses(string environmentName,
            DateTime cutoffDate);
        bool IsEnvironmentOwner(string envName, ClaimsPrincipal user);
        bool DeleteEnvironment(EnvironmentApiModel env, IPrincipal principal);
        EnvironmentApiModel UpdateEnvironment(EnvironmentApiModel env, IPrincipal user);
        IEnumerable<string> GetEnvironmentNames(IPrincipal principal);
        EnvironmentApiModel GetEnvironment(string environmentName);
        IEnumerable<ProjectApiModel> GetMappedProjects(string envName);
        IEnumerable<EnvironmentData> AccessibleEnvironmentsAccessLevel(IDeploymentContext context,
            string projectName, IPrincipal user, AccessLevel accessLevel);
        bool EnvironmentIsProd(string envName);
        bool EnvironmentIsSecure(string envName);
        IEnumerable<EnvironmentApiModel> GetPossibleEnvironmentChildren(int id, IPrincipal user);
        void SetParentForEnvironment(int? parentEnvId, int childEnvId, IPrincipal user);
        EnvironmentApiModel MapToEnvironmentApiModel(Environment env, bool userEditable, bool isOwner);
    }
}
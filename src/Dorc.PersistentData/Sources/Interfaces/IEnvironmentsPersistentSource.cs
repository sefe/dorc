using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using System.Security.Principal;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IEnvironmentsPersistentSource
    {
        EnvironmentApiModel GetEnvironment(string environmentName, IPrincipal user);
        bool EnvironmentExists(string rowEnvironment);
        string GetEnvironmentOwner(int envId);
        bool SetEnvironmentOwner(IPrincipal updatedBy, int envId, ActiveDirectoryElementApiModel user);
        EnvironmentApiModel AttachServerToEnv(int envId, int serverId, IPrincipal user);
        EnvironmentApiModel DetachServerFromEnv(int envId, int serverId, IPrincipal user);
        EnvironmentApiModel AttachDatabaseToEnv(int envId, int databaseId, IPrincipal user);
        EnvironmentApiModel DetachDatabaseFromEnv(int envId, int databaseId, IPrincipal user);
        Environment GetSecurityObject(string environmentName);
        EnvironmentApiModel CreateEnvironment(EnvironmentApiModel env, IPrincipal principal);
        IEnumerable<EnvironmentApiModel> GetEnvironments(IPrincipal user);
        EnvironmentApiModel GetEnvironment(int environmentId, IPrincipal user);
        IEnumerable<EnvironmentComponentStatusModel> GetEnvironmentComponentStatuses(int environmentId);
        IEnumerable<EnvironmentContentBuildsApiModel> GetEnvironmentComponentStatuses(string environmentName,
            DateTime cutoffDate);
        IEnumerable<string> GetEnvironmentNames(AccessLevel accessLevel, IPrincipal user, string thinClientServer,
            bool excludeProd);
        bool IsEnvironmentOwner(string envName, IPrincipal user);
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
    }
}
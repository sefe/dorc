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
        List<string> GetEnvironmentOwnerIds(int envId);
        bool SetEnvironmentOwner(IPrincipal updatedBy, int envId, UserElementApiModel user);
        EnvironmentApiModel AttachServerToEnv(int envId, int serverId, ClaimsPrincipal user);
        EnvironmentApiModel DetachServerFromEnv(int envId, int serverId, ClaimsPrincipal user);
        EnvironmentApiModel AttachDatabaseToEnv(int envId, int databaseId, ClaimsPrincipal user);
        EnvironmentApiModel DetachDatabaseFromEnv(int envId, int databaseId, ClaimsPrincipal user);
        Environment GetSecurityObject(string environmentName);
        EnvironmentApiModel CreateEnvironment(EnvironmentApiModel env, ClaimsPrincipal principal);
        IEnumerable<EnvironmentApiModel> GetEnvironments(IPrincipal user);
        EnvironmentApiModel? GetEnvironment(int environmentId);
        EnvironmentApiModel? GetEnvironment(int environmentId, ClaimsPrincipal? user = null);
        IEnumerable<EnvironmentComponentStatusModel> GetEnvironmentComponentStatuses(int environmentId);
        IEnumerable<EnvironmentContentBuildsApiModel> GetEnvironmentComponentStatuses(string environmentName,
            DateTime cutoffDate);
        bool IsEnvironmentOwner(string envName, ClaimsPrincipal user);
        bool DeleteEnvironment(EnvironmentApiModel env, IPrincipal principal);
        bool DeleteEnvironmentInternal(int environmentId, bool onlyIfProvisioningWithoutRequests = false);
        EnvironmentApiModel UpdateEnvironment(EnvironmentApiModel env, IPrincipal user);
        IEnumerable<string> GetEnvironmentNames(IPrincipal principal);
        IEnumerable<EnvironmentApiModel> GetEnvironmentsForDatabase(string databaseName, string serverName, IPrincipal user);
        EnvironmentApiModel GetEnvironment(string environmentName);
        IEnumerable<ProjectApiModel> GetMappedProjects(string envName);
        IEnumerable<EnvironmentData> AccessibleEnvironmentsAccessLevel(IDeploymentContext context,
            string projectName, IPrincipal user, AccessLevel accessLevel);
        bool EnvironmentIsProd(string envName);
        bool EnvironmentIsSecure(string envName);
        IEnumerable<EnvironmentApiModel> GetPossibleEnvironmentChildren(int id, IPrincipal user);
        void SetParentForEnvironment(int? parentEnvId, int childEnvId, IPrincipal user);
        EnvironmentApiModel MapToEnvironmentApiModel(Environment env, bool userEditable, bool isOwner);
        EnvironmentApiModel CloneEnvironment(CloneEnvironmentRequest request, ClaimsPrincipal user);
        IEnumerable<EnvironmentApiModel> GetEphemeralEnvironments(int? projectId = null, string? state = null, IPrincipal? user = null);
        IEnumerable<EnvironmentApiModel> GetEphemeralEnvironments(int? projectId, IPrincipal? user);
        IEnumerable<EnvironmentApiModel> GetExpiredEphemeralEnvironments(DateTime utcNow, bool allowReclaimStrandedTearingDown = false);
        bool TryClaimExpiredEphemeralEnvironmentForTeardown(int environmentId, DateTime utcNow, bool allowReclaimStrandedTearingDown = false);
        bool TryClaimEphemeralEnvironmentForTeardown(int environmentId, bool requireExpired, DateTime utcNow, out string? previousState, bool allowReclaimStrandedTearingDown = false);
        bool TryTransitionEphemeralState(string environmentName, IReadOnlyCollection<string> allowedCurrentStates, string nextState, int? expectedEnvironmentId = null);
        EnvironmentApiModel? UpdateEphemeralState(int environmentId, string ephemeralState, DateTime? expiresAtUtc = null);
        EnvironmentApiModel? UpdateEphemeralState(string environmentName, string ephemeralState, DateTime? expiresAtUtc = null);
        EnvironmentApiModel? ExtendEphemeralTtl(int environmentId, DateTime newExpiresAtUtc, int maxTtlMinutesCap = 1440, IPrincipal? user = null);
        EnvironmentApiModel? ExtendEphemeralTtl(string environmentName, DateTime newExpiresAtUtc, int maxTtlMinutesCap, IPrincipal? user = null, int? expectedEnvironmentId = null, int? additionalMinutes = null);
    }
}

using System.Security.Claims;

namespace Dorc.Core.Interfaces
{
    public interface ISecurityPrivilegesChecker
    {
        bool CanModifyProperty(ClaimsPrincipal user);
        bool CanModifyPropertyValue(ClaimsPrincipal user, string environmentName);
        bool IsEnvironmentOwnerOrAdmin(ClaimsPrincipal user, string environmentName);
        bool IsProjectOwnerOrAdmin(ClaimsPrincipal user, string projectName);
        bool CanModifyProject(ClaimsPrincipal user, string projectName);
        bool CanModifyEnvironment(ClaimsPrincipal user, string envName);
        bool CanReadSecrets(ClaimsPrincipal user, string environmentName);

        /// <summary>
        /// Whether the caller may grant the read-secrets privilege. Separate from exercising
        /// it: administration is a human activity, retrieval is a machine one.
        /// </summary>
        bool CanGrantReadSecrets(ClaimsPrincipal user, string environmentName);
        bool CanModifyProject(ClaimsPrincipal user, int projectId);
    }
}
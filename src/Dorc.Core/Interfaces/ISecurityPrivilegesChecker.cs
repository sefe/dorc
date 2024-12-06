using System.Security.Claims;

namespace Dorc.Core.Interfaces
{
    public interface ISecurityPrivilegesChecker
    {
        bool CanModifyProperty(ClaimsPrincipal user);
        bool CanModifyPropertyValue(ClaimsPrincipal user, string environmentName);
        bool IsEnvironmentOwnerOrAdmin(ClaimsPrincipal user, string environmentName);
        bool CanModifyProject(ClaimsPrincipal user, string projectName);
        bool CanModifyEnvironment(ClaimsPrincipal user, string envName);
        bool IsEnvironmentOwnerOrAdminOrDelegate(ClaimsPrincipal user, string environmentName);
        bool IsEnvironmentOwnerOrDelegate(ClaimsPrincipal user, string environmentName);
        bool CanReadSecrets(ClaimsPrincipal user, string environmentName);
        bool CanModifyProject(ClaimsPrincipal user, int projectId);
    }
}
using System.Security.Claims;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;

namespace Dorc.Core.Security;

/// <summary>
/// Container, cloud and API definitions are shared across environments. Creating
/// a definition requires a role; editing a mapped definition requires write
/// access to every mapping; membership changes require only the target environment.
/// Callers must read mappings and authorize writes within their mutation transaction.
/// </summary>
public sealed class SharedResourceAccessPolicy(
    IRolePrivilegesChecker roles,
    ISecurityPrivilegesChecker security)
{
    public bool CanCreateDefinition(ClaimsPrincipal user) =>
        roles.IsPowerUser(user) || roles.IsAdmin(user);

    public bool CanManageMembership(ClaimsPrincipal user, string environmentName) =>
        security.CanModifyEnvironment(user, environmentName);

    public SharedResourceDefinitionAccess DefinitionAccess(ClaimsPrincipal user, IEnumerable<string>? environmentNames)
    {
        var names = environmentNames?.ToList() ?? [];
        if (names.Count == 0)
            return new SharedResourceDefinitionAccess(CanCreateDefinition(user), null);

        var deniedEnvironment = names.FirstOrDefault(name => !CanManageMembership(user, name));
        return new SharedResourceDefinitionAccess(deniedEnvironment == null, deniedEnvironment);
    }
}

public sealed record SharedResourceDefinitionAccess(bool IsAllowed, string? DeniedEnvironment);

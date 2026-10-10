using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dorc.ApiModel;

namespace Dorc.Core.DatabaseAccess;

public sealed class DatabaseAccessConflictException(string message) : Exception(message);

public static class DatabaseAccessPlanner
{
    public const string SqlServer = "sql-server";
    public static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    public static string Fingerprint(object value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    public static bool ProtectedPrincipal(string name) =>
        new[] { "dbo", "guest", "sys", "INFORMATION_SCHEMA", "public" }.Contains(name, Names)
        || name.StartsWith("##", StringComparison.Ordinal);

    public static bool ProtectedRole(string name) =>
        new[] { "public", "db_owner", "db_securityadmin", "db_accessadmin" }.Contains(name, Names);

    public static void Identifier([System.Diagnostics.CodeAnalysis.NotNull] string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name != name.Trim() || name.Any(char.IsControl))
            throw new ArgumentException("Identifiers must contain 1-128 characters, without control characters or surrounding whitespace.");
    }

    public static void Validate(DatabaseAccessState state)
    {
        if (string.IsNullOrWhiteSpace(state.Provider) || state.Provider.Length > 40)
            throw new ArgumentException("An explicit database access provider is required.");
        if (state.Principals == null || state.Roles == null || state.Memberships == null)
            throw new ArgumentException("Principals, roles and memberships must be supplied.");
        if (state.Principals.Count > 1000 || state.Roles.Count > 1000 || state.Memberships.Count > 10000)
            throw new ArgumentException("A change is limited to 1000 principals, 1000 roles and 10000 memberships.");
        if (state.Principals.Any(p => p == null) || state.Roles.Any(r => r == null) || state.Memberships.Any(m => m == null))
            throw new ArgumentException("Null principals, roles or memberships are not allowed.");
        var principals = new HashSet<string>(Names);
        foreach (var principal in state.Principals)
        {
            Identifier(principal.Name);
            if (!principals.Add(principal.Name) || ProtectedPrincipal(principal.Name))
                throw new ArgumentException($"Duplicate or protected principal '{principal.Name}'.");
            if (principal.Kind == "Native")
            {
                Identifier(principal.LoginName);
                if (principal.DirectoryId != null || ProtectedPrincipal(principal.LoginName))
                    throw new ArgumentException("Native principals require a non-system login and no directory reference.");
            }
            else if (principal.Kind is "DirectoryUser" or "DirectoryGroup")
            {
                if (string.IsNullOrWhiteSpace(principal.DirectoryId) || principal.DirectoryId.Length > 256
                    || principal.LoginName != null)
                    throw new ArgumentException("Directory principals require a stable ID and no cached login/profile.");
            }
            else throw new ArgumentException($"Unsupported principal kind '{principal.Kind}'.");
        }
        var roles = new HashSet<string>(Names);
        foreach (var role in state.Roles)
        {
            Identifier(role.Name);
            if (!roles.Add(role.Name) || principals.Contains(role.Name) || ProtectedRole(role.Name)
                || (role.Managed && role.Name.StartsWith("db_", StringComparison.OrdinalIgnoreCase))
                || (!role.Managed && !role.Present))
                throw new ArgumentException($"Duplicate, conflicting or protected role '{role.Name}'.");
        }
        var memberships = new HashSet<string>(Names);
        foreach (var member in state.Memberships)
        {
            Identifier(member.Principal);
            Identifier(member.Role);
            if (!principals.Contains(member.Principal) || !roles.Contains(member.Role)
                || !memberships.Add(member.Principal + "\0" + member.Role))
                throw new ArgumentException("Memberships must uniquely reference principals and roles in this database.");
            if (member.Present && (!state.Principals.Single(p => Names.Equals(p.Name, member.Principal)).Present
                || !state.Roles.Single(r => Names.Equals(r.Name, member.Role)).Present))
                throw new ArgumentException("A present membership cannot reference an absent principal or role.");
        }
    }

    public static DatabaseAccessPreview Plan(DatabaseAccessState desired, DatabaseAccessObservation observed)
    {
        if (desired.Provider != SqlServer)
            throw new NotSupportedException($"The SQL Server planner cannot handle provider '{desired.Provider}'.");
        Validate(desired);
        var result = new DatabaseAccessPreview { Revision = desired.Revision, Observed = observed };
        if (desired.Principals.Where(p => p.Present)
            .GroupBy(p => p.Kind == "Native" ? "Native:" + p.LoginName : "Directory:" + p.DirectoryId, Names)
            .Any(group => group.Count() > 1))
            result.Errors.Add("SQL Server supports only one database user per server login. Duplicate desired login mappings must be resolved.");
        foreach (var role in desired.Roles.Where(r => r.Present
            && !observed.Roles.Any(observedRole => Names.Equals(observedRole.Name, r.Name))))
        {
            if (!role.Managed) result.Errors.Add($"Role '{role.Name}' does not exist; it is reference-only.");
            else result.Operations.Add(new() { Action = "CreateRole", Role = role.Name });
        }
        foreach (var principal in desired.Principals.Where(p => p.Present))
        {
            var current = observed.Principals.SingleOrDefault(p => Names.Equals(p.Name, principal.Name));
            var login = observed.Logins.SingleOrDefault(l => principal.Kind == "Native"
                ? Names.Equals(l.Name, principal.LoginName) && l.Kind == "Native"
                : l.DirectoryId == principal.DirectoryId && l.Kind == principal.Kind);
            if (login == null || login.Protected)
            {
                result.Errors.Add($"Principal '{principal.Name}' requires an existing, visible, non-system server login of the correct type. Server-login provisioning is not supported.");
                continue;
            }
            if (observed.Principals.Any(p => !Names.Equals(p.Name, principal.Name) && Names.Equals(p.LoginName, login.Name)))
            {
                result.Errors.Add($"Login '{login.Name}' is already mapped to another database user. Resolve that mapping before creating or remapping '{principal.Name}'.");
                continue;
            }
            if (current == null)
                result.Operations.Add(new() { Action = "CreateUser", Principal = principal.Name, Login = login.Name });
            else if (current.Protected || current.Kind != principal.Kind)
                result.Errors.Add($"Principal '{principal.Name}' is protected or has a different authentication type.");
            else if (!Names.Equals(current.LoginName, login.Name)
                || (principal.Kind != "Native" && current.DirectoryId != principal.DirectoryId))
                result.Operations.Add(new() { Action = "MapUser", Principal = principal.Name, Login = login.Name });
        }
        foreach (var member in desired.Memberships)
        {
            var exists = observed.Memberships.Any(m => Names.Equals(m.Principal, member.Principal) && Names.Equals(m.Role, member.Role));
            if (member.Present != exists)
                result.Operations.Add(new() { Action = member.Present ? "GrantRole" : "RevokeRole", Principal = member.Principal, Role = member.Role });
        }
        foreach (var principal in desired.Principals.Where(p => !p.Present))
        {
            var current = observed.Principals.SingleOrDefault(p => Names.Equals(p.Name, principal.Name));
            if (current == null) continue;
            if (current.Protected) result.Errors.Add($"Principal '{principal.Name}' is protected.");
            else
            {
                if (observed.Memberships.Any(m => Names.Equals(m.Principal, principal.Name)
                    && !desired.Memberships.Any(d => !d.Present && Names.Equals(d.Principal, m.Principal) && Names.Equals(d.Role, m.Role))))
                    result.Errors.Add($"Principal '{principal.Name}' has unmanaged memberships. Revoke them explicitly before removal.");
                result.Operations.Add(new() { Action = "DropUser", Principal = principal.Name });
            }
        }
        foreach (var role in desired.Roles.Where(r => r.Managed && !r.Present
            && observed.Roles.Any(observedRole => Names.Equals(observedRole.Name, r.Name))))
        {
            if (observed.Memberships.Any(m => Names.Equals(m.Role, role.Name)
                && !desired.Memberships.Any(d => !d.Present && Names.Equals(d.Principal, m.Principal) && Names.Equals(d.Role, m.Role))))
                result.Errors.Add($"Role '{role.Name}' still has unmanaged members.");
            result.Operations.Add(new() { Action = "DropRole", Role = role.Name });
        }
        result.Token = Fingerprint(new { desired, observed, result.Operations, result.Errors });
        return result;
    }
}

using Dorc.ApiModel;

namespace Dorc.Core.DatabaseAccess;

public record LegacyDatabaseAccessAssignment(int UserId, string Login, string Kind, string Role);

public static class DatabaseAccessImport
{
    public static DatabaseAccessImportPreview Build(DatabaseAccessState current,
        IReadOnlyList<LegacyDatabaseAccessAssignment> source, DatabaseAccessImportRequest request)
    {
        if (request.RoleMappings == null || request.DirectoryMappings == null)
            throw new ArgumentException("Role and directory mappings must be supplied.");
        var proposed = System.Text.Json.JsonSerializer.Deserialize<DatabaseAccessState>(
            System.Text.Json.JsonSerializer.Serialize(current))!;
        var result = new DatabaseAccessImportPreview { Proposed = proposed, SourceAssignments = source.Count };
        foreach (var item in source)
        {
            if (!request.RoleMappings.TryGetValue(item.Role, out var role) || string.IsNullOrWhiteSpace(role))
            {
                result.Errors.Add($"Permission '{item.Role}' requires an explicit target-role mapping.");
                continue;
            }
            var kind = item.Kind.Trim().ToLowerInvariant();
            if (kind is not ("sql" or "endur" or "windows"))
            {
                result.Errors.Add($"Legacy user {item.UserId} has unsupported login type '{item.Kind}'.");
                continue;
            }
            var name = item.Login.Trim();
            if (string.IsNullOrEmpty(name))
            {
                result.Errors.Add($"Legacy user {item.UserId} has no database login.");
                continue;
            }
            string? directoryId = null;
            if (kind == "windows" && !request.DirectoryMappings.TryGetValue(item.UserId, out directoryId))
            {
                result.Errors.Add($"Windows user {item.UserId} requires an explicit live directory reference.");
                continue;
            }
            var existing = proposed.Principals.SingleOrDefault(p => DatabaseAccessPlanner.Names.Equals(p.Name, name));
            if (existing != null && (existing.LegacyUserId != item.UserId || !existing.Present
                || (kind == "windows" ? existing.DirectoryId != directoryId || existing.Kind == "Native" : existing.LoginName != name || existing.Kind != "Native")))
            {
                result.Errors.Add($"Principal '{name}' conflicts with existing desired state or has diverged.");
                continue;
            }
            if (existing == null)
                proposed.Principals.Add(new()
                {
                    Name = name, Kind = kind == "windows" ? "DirectoryUser" : "Native",
                    LoginName = kind == "windows" ? null : name, DirectoryId = directoryId, LegacyUserId = item.UserId
                });
            var existingRole = proposed.Roles.SingleOrDefault(r => DatabaseAccessPlanner.Names.Equals(r.Name, role));
            if (existingRole == null) proposed.Roles.Add(new() { Name = role, Managed = false });
            else if (!existingRole.Present) result.Errors.Add($"Role '{role}' is marked absent.");
            var membership = proposed.Memberships.SingleOrDefault(m =>
                DatabaseAccessPlanner.Names.Equals(m.Principal, name) && DatabaseAccessPlanner.Names.Equals(m.Role, role));
            if (membership == null) proposed.Memberships.Add(new() { Principal = name, Role = role });
            else if (!membership.Present) result.Errors.Add($"Membership '{name}'/'{role}' has diverged; it is marked absent.");
        }
        var sourceIds = source.Select(s => s.UserId).ToHashSet();
        foreach (var p in proposed.Principals.Where(p => p.LegacyUserId.HasValue && !sourceIds.Contains(p.LegacyUserId.Value)))
            result.Errors.Add($"Imported principal '{p.Name}' no longer exists in this database's legacy assignments. Resolve divergence explicitly.");
        foreach (var membership in current.Memberships.Where(m => m.Present))
        {
            var principal = current.Principals.Single(p => DatabaseAccessPlanner.Names.Equals(p.Name, membership.Principal));
            if (principal.LegacyUserId is int legacyUserId && !source.Any(s => s.UserId == legacyUserId
                && DatabaseAccessPlanner.Names.Equals(s.Login.Trim(), principal.Name)
                && request.RoleMappings.TryGetValue(s.Role, out var mapped) && DatabaseAccessPlanner.Names.Equals(mapped, membership.Role)))
                result.Errors.Add($"Imported membership '{membership.Principal}'/'{membership.Role}' is absent from the current legacy mapping. Resolve divergence explicitly.");
        }
        try { DatabaseAccessPlanner.Validate(proposed); }
        catch (ArgumentException e) { result.Errors.Add(e.Message); }
        result.Token = DatabaseAccessPlanner.Fingerprint(new { current, source, request.RoleMappings, request.DirectoryMappings, proposed, result.Errors });
        return result;
    }
}

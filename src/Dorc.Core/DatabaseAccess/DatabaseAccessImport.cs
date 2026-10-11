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
        if (request.ExcludedLegacyUserIds == null)
            throw new ArgumentException("Excluded legacy user IDs must be supplied as a list.");
        if (request.ApprovedDbOwnerLegacyUserIds == null)
            throw new ArgumentException("Approved db_owner legacy user IDs must be supplied as a list.");
        var proposed = System.Text.Json.JsonSerializer.Deserialize<DatabaseAccessState>(
            System.Text.Json.JsonSerializer.Serialize(current))!;
        var result = new DatabaseAccessImportPreview { Proposed = proposed, SourceAssignments = source.Count };
        var approvedIds = new HashSet<int>();
        foreach (var id in request.ApprovedDbOwnerLegacyUserIds)
        {
            if (id <= 0 || !source.Any(s => s.UserId == id
                && request.RoleMappings.TryGetValue(s.Role, out var mapped) && DatabaseAccessPlanner.IsDbOwner(mapped)))
                result.Errors.Add($"Legacy user {id} has no db_owner mapping in this database to approve.");
            else if (request.ExcludedLegacyUserIds.Contains(id))
                result.Errors.Add($"Legacy user {id} cannot be both excluded and approved for db_owner.");
            else if (!approvedIds.Add(id))
                result.Errors.Add($"Legacy user {id} is approved for db_owner more than once.");
        }
        var excludedIds = new HashSet<int>();
        foreach (var id in request.ExcludedLegacyUserIds)
        {
            var assignments = source.Where(s => s.UserId == id).ToList();
            if (id <= 0 || assignments.Count == 0)
                result.Errors.Add($"Legacy user {id} cannot be excluded: it has no assignments in this database.");
            else if (excludedIds.Contains(id))
                result.Errors.Add($"Legacy user {id} is excluded more than once.");
            else if (assignments.Any(s => !s.Kind.Trim().Equals("windows", StringComparison.OrdinalIgnoreCase))
                || assignments.Select(s => s.Login.Trim()).Distinct(DatabaseAccessPlanner.Names).Count() != 1
                || assignments.Any(s => string.IsNullOrWhiteSpace(s.Login)))
                result.Errors.Add($"Legacy user {id} cannot be excluded: exclusions require a consistent Windows account.");
            else if (request.DirectoryMappings.ContainsKey(id))
                result.Errors.Add($"Legacy user {id} cannot be both mapped to a directory identity and excluded.");
            else if (current.Principals.Any(p => p.LegacyUserId == id
                || assignments.Any(s => DatabaseAccessPlanner.Names.Equals(s.Login.Trim(), p.Name))))
                result.Errors.Add($"Legacy user {id} cannot be excluded: its principal is already in desired state. Resolve it explicitly.");
            else
            {
                excludedIds.Add(id);
                result.ExcludedAccounts.Add(new()
                {
                    LegacyUserId = id, LoginName = assignments[0].Login.Trim(),
                    Permissions = assignments.Select(s => s.Role).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToList(),
                    AssignmentCount = assignments.Count
                });
            }
        }
        foreach (var item in source)
        {
            if (excludedIds.Contains(item.UserId)) continue;
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
            var ownerMembership = DatabaseAccessPlanner.IsDbOwner(role);
            var previouslyApproved = current.Principals.Any(p => p.LegacyUserId == item.UserId
                && DatabaseAccessPlanner.Names.Equals(p.Name, name))
                && current.Memberships.Any(m => m.Present && DatabaseAccessPlanner.IsDbOwner(m.Role)
                    && DatabaseAccessPlanner.Names.Equals(m.Principal, name));
            if (ownerMembership && !previouslyApproved && !approvedIds.Contains(item.UserId))
            {
                result.Errors.Add($"Legacy user {item.UserId} requires explicit db_owner membership approval from an environment owner or administrator.");
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
            else if (!membership.Present)
            {
                if (ownerMembership && approvedIds.Contains(item.UserId)) membership.Present = true;
                else result.Errors.Add($"Membership '{name}'/'{role}' has diverged; it is marked absent.");
            }
            if (ownerMembership && approvedIds.Contains(item.UserId) && !result.ApprovedDbOwnerLegacyUserIds.Contains(item.UserId))
                result.ApprovedDbOwnerLegacyUserIds.Add(item.UserId);
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
        result.Token = DatabaseAccessPlanner.Fingerprint(new
        {
            current, source, request.RoleMappings, request.DirectoryMappings, request.ExcludedLegacyUserIds,
            request.ApprovedDbOwnerLegacyUserIds, proposed, result.ExcludedAccounts,
            AcknowledgedDbOwnerLegacyUserIds = result.ApprovedDbOwnerLegacyUserIds, result.Errors
        });
        return result;
    }
}

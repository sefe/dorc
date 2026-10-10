using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.DatabaseAccess;

public sealed class DatabaseAccessService(
    DatabaseAccessStore store, IDeploymentContextFactory factory, DatabaseAccessProviders providers,
    DatabaseAccessDirectory directory, ILogger<DatabaseAccessService> logger)
{
    private DatabaseAccessTarget Target(int id)
    {
        using var context = factory.GetContext();
        var db = context.Databases.SingleOrDefault(d => d.Id == id) ?? throw new ArgumentException("Database no longer exists.");
        return new(id, db.ServerName ?? "", db.Name ?? "");
    }

    private void ValidateDirectory(DatabaseAccessState state)
    {
        foreach (var p in state.Principals.Where(p => p.Present && p.Kind != "Native"))
        {
            var identity = directory.Resolve(p.DirectoryId);
            if ((p.Kind == "DirectoryGroup") != identity.IsGroup)
                throw new ArgumentException($"Directory identity type does not match principal '{p.Name}'.");
        }
    }

    public DatabaseAccessState Get(int id)
    {
        using var context = store.Open(id);
        return DatabaseAccessStore.Read(context, id);
    }

    public DatabaseAccessState Save(int id, DatabaseAccessState desired, string actor)
    {
        if (desired.DatabaseId != id) throw new ArgumentException("Database ID does not match the route.");
        DatabaseAccessPlanner.Validate(desired);
        providers.Resolve(desired.Provider);
        ValidateDirectory(desired);
        using var context = store.Open(id);
        var current = DatabaseAccessStore.Read(context, id);
        ValidateTransition(current, desired);
        return DatabaseAccessStore.Save(context, desired, actor, "Save");
    }

    public static void ValidateTransition(DatabaseAccessState current, DatabaseAccessState desired)
    {
        if (current.Revision != desired.Revision)
            throw new DatabaseAccessConflictException("Desired state changed. Reload before saving.");
        if (current.Revision > 0 && current.Provider != desired.Provider)
            throw new ArgumentException("Cannot change the provider of an existing managed database.");
        foreach (var p in current.Principals)
        {
            var next = desired.Principals.SingleOrDefault(n => DatabaseAccessPlanner.Names.Equals(n.Name, p.Name));
            if (next == null || p.LegacyUserId != next.LegacyUserId || p.Kind != next.Kind
                || p.DirectoryId != next.DirectoryId)
                throw new ArgumentException("Keep existing principals and their provenance/identity. Use Present=false for removal.");
        }
        foreach (var r in current.Roles)
        {
            var next = desired.Roles.SingleOrDefault(n => DatabaseAccessPlanner.Names.Equals(n.Name, r.Name));
            if (next == null || next.Managed != r.Managed)
                throw new ArgumentException("Keep existing roles and their ownership mode. Use Present=false for managed-role removal.");
        }
        foreach (var m in current.Memberships)
            if (!desired.Memberships.Any(n => DatabaseAccessPlanner.Names.Equals(n.Principal, m.Principal) && DatabaseAccessPlanner.Names.Equals(n.Role, m.Role)))
                throw new ArgumentException("Keep existing memberships; use Present=false for explicit revocation.");
        if (desired.Principals.Any(p => !current.Principals.Any(c => DatabaseAccessPlanner.Names.Equals(c.Name, p.Name))
            && (!p.Present || p.LegacyUserId.HasValue)))
            throw new ArgumentException("New principals must be present and cannot claim legacy provenance outside import.");
        if (desired.Roles.Any(r => !r.Present && !current.Roles.Any(c => DatabaseAccessPlanner.Names.Equals(c.Name, r.Name))))
            throw new ArgumentException("Cannot drop an untracked role.");
    }

    public DatabaseAccessPreview Preview(int id)
    {
        using var context = store.Open(id);
        var state = DatabaseAccessStore.Read(context, id);
        ValidateDirectory(state);
        return providers.Resolve(state.Provider).Preview(Target(id), state);
    }

    public DatabaseAccessPreview Apply(int id, string token, string actor)
    {
        using var context = store.Open(id);
        var state = DatabaseAccessStore.Read(context, id);
        ValidateDirectory(state);
        var target = Target(id);
        var provider = providers.Resolve(state.Provider);
        var before = provider.Preview(target, state);
        if (before.Token != token || before.Errors.Count > 0)
            throw new DatabaseAccessConflictException("Preview is stale or invalid. Generate a fresh preview.");
        var audit = DatabaseAccessStore.Audit(context, id, actor, "Reconcile", "Started",
            System.Text.Json.JsonSerializer.Serialize(before));
        try
        {
            var after = provider.Apply(target, state, token);
            audit.Status = "Succeeded";
            // Retain the confirmed operation list; after-state is a separate observation.
            audit.Detail = System.Text.Json.JsonSerializer.Serialize(new { Before = before, After = after });
            context.SaveChanges();
            logger.LogInformation("Database access reconciliation succeeded for database {DatabaseId}, audit {AuditId}", id, audit.Id);
            return after;
        }
        catch (Exception error)
        {
            logger.LogError(error, "Database access reconciliation failed or has an uncertain outcome for database {DatabaseId}, audit {AuditId}", id, audit.Id);
            audit.Status = "FailedOrUncertain";
            audit.Detail = System.Text.Json.JsonSerializer.Serialize(new { Before = before, Error = error.Message });
            context.SaveChanges();
            throw;
        }
    }

    public DatabaseAccessImportPreview Import(int id, DatabaseAccessImportRequest request, bool apply, string actor)
    {
        using var context = store.Open(id);
        var current = DatabaseAccessStore.Read(context, id);
        using var legacy = factory.GetContext();
        var source = (from map in legacy.EnvironmentUsers
                      join user in legacy.Users on map.UserId equals user.Id
                      join permission in legacy.Permissions on map.PermissionId equals permission.Id
                      where map.DbId == id
                      orderby user.Id, permission.Id
                      select new LegacyDatabaseAccessAssignment(user.Id, user.LoginId ?? "", user.LoginType ?? "", permission.Name ?? "")).ToList();
        var preview = DatabaseAccessImport.Build(current, source, request);
        if (source.Count != legacy.EnvironmentUsers.Count(map => map.DbId == id))
            preview.Errors.Add("Legacy mappings contain missing users or permissions. Repair the orphaned mappings before import.");
        foreach (var p in preview.Proposed.Principals.Where(p => p.Kind != "Native" && p.Present))
        {
            try
            {
                var identity = directory.Resolve(p.DirectoryId);
                p.Kind = identity.IsGroup ? "DirectoryGroup" : "DirectoryUser";
            }
            catch (ArgumentException error)
            {
                preview.Errors.Add($"Principal '{p.Name}': {error.Message}");
            }
        }
        if (preview.Errors.Count == 0)
        {
            var targetPreview = providers.Resolve(preview.Proposed.Provider).Preview(Target(id), preview.Proposed);
            preview.Errors.AddRange(targetPreview.Errors);
        }
        preview.Token = DatabaseAccessPlanner.Fingerprint(new { preview.Token, preview.Proposed, preview.Errors });
        if (apply)
        {
            if (preview.Token != request.Token || preview.Errors.Count > 0)
                throw new DatabaseAccessConflictException("Import preview is stale or contains errors. Preview the import again.");
            if (DatabaseAccessPlanner.Fingerprint(Canonical(current)) != DatabaseAccessPlanner.Fingerprint(Canonical(preview.Proposed)))
                preview.Proposed = DatabaseAccessStore.Save(context, preview.Proposed, actor, "Import");
            else
                DatabaseAccessStore.Audit(context, id, actor, "Import", "NoChanges", "Legacy import matched existing desired state.");
        }
        return preview;
    }

    private static object Canonical(DatabaseAccessState state) => new
    {
        state.DatabaseId, state.Provider, state.Revision,
        Principals = state.Principals.OrderBy(p => p.Name, StringComparer.Ordinal),
        Roles = state.Roles.OrderBy(r => r.Name, StringComparer.Ordinal),
        Memberships = state.Memberships.OrderBy(m => m.Principal, StringComparer.Ordinal).ThenBy(m => m.Role, StringComparer.Ordinal)
    };

    public List<DatabaseAccessAuditApiModel> Audit(int id)
    {
        using var context = store.Open(id);
        return context.Set<DatabaseAccessAudit>().Where(a => a.DatabaseId == id).OrderByDescending(a => a.Id).Take(100)
            .Select(a => new DatabaseAccessAuditApiModel
            {
                Id = a.Id, CreatedUtc = a.CreatedUtc, Actor = a.Actor, Action = a.Action, Status = a.Status, Detail = a.Detail
            }).ToList();
    }
}

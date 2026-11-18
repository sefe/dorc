using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.DatabaseAccess;

public sealed class DatabaseAccessReconciler(
    DatabaseAccessStore store,
    IDeploymentContextFactory factory,
    DatabaseAccessProviders providers,
    DatabaseAccessDirectory directory,
    ILogger<DatabaseAccessReconciler> logger)
{
    private Database DatabaseRecord(int id)
    {
        using var context = factory.GetContext();
        return context.Databases.SingleOrDefault(d => d.Id == id)
            ?? throw new ArgumentException("Database no longer exists.");
    }

    private static DatabaseAccessTarget Target(Database db) =>
        new(db.Id, db.ServerName ?? "", db.Name ?? "");

    private static DatabaseAccessState ReadState(DatabaseAccessContext context, Database db) =>
        DatabaseAccessStore.Read(context, db.Id, DatabaseAccessPlanner.ProviderFor(db.DatabaseType));

    private void ValidateDirectory(DatabaseAccessState state)
    {
        foreach (var p in state.Principals.Where(p => p.Present && p.Kind != "Native"))
        {
            var identity = directory.Resolve(p.DirectoryId);
            if ((p.Kind == "DirectoryGroup") != identity.IsGroup)
                throw new ArgumentException($"Directory identity type does not match principal '{p.Name}'.");
        }
    }

    internal static void EnsureProviderMatchesDatabaseType(string provider, Database db)
    {
        var expectedProvider = DatabaseAccessPlanner.ProviderFor(db.DatabaseType);
        if (provider != expectedProvider)
            throw new ArgumentException($"Database provider '{provider}' does not match database type '{expectedProvider}'.");
    }

    public DatabaseAccessState Get(int id)
    {
        var db = DatabaseRecord(id);
        using var context = store.Open(id);
        return ReadState(context, db);
    }

    public DatabaseAccessState Save(int id, DatabaseAccessState desired, string actor, bool canApproveDbOwner = false)
    {
        if (desired.DatabaseId != id) throw new ArgumentException("Database ID does not match the route.");
        DatabaseAccessPlanner.Validate(desired);
        providers.Resolve(desired.Provider);
        var db = DatabaseRecord(id);
        EnsureProviderMatchesDatabaseType(desired.Provider, db);
        ValidateDirectory(desired);
        using var context = store.Open(id);
        var current = ReadState(context, db);
        EnsureProviderMatchesDatabaseType(current.Provider, db);
        ValidateTransition(current, desired, canApproveDbOwner);
        return DatabaseAccessStore.Save(context, desired, actor, "Save");
    }

    public static void ValidateTransition(DatabaseAccessState current, DatabaseAccessState desired, bool canApproveDbOwner = false)
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
            if (!DatabaseAccessPlanner.Names.Equals(p.LoginName, next.LoginName)
                && current.Memberships.Any(m => DatabaseAccessPlanner.IsDbOwner(m.Role)
                    && DatabaseAccessPlanner.Names.Equals(m.Principal, p.Name)))
                throw new ArgumentException("The login bound to an approved db_owner principal cannot be changed through ordinary desired-state editing.");
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
        if (!canApproveDbOwner && desired.Memberships.Any(m => m.Present && DatabaseAccessPlanner.IsDbOwner(m.Role)
            && !current.Memberships.Any(c => c.Present && DatabaseAccessPlanner.IsDbOwner(c.Role)
                && DatabaseAccessPlanner.Names.Equals(c.Principal, m.Principal))))
            throw new ArgumentException("Granting or re-enabling db_owner membership requires an owner or administrator of every attached environment.");
        if (desired.Principals.Any(p => !current.Principals.Any(c => DatabaseAccessPlanner.Names.Equals(c.Name, p.Name))
            && (!p.Present || p.LegacyUserId.HasValue)))
            throw new ArgumentException("New principals must be present and cannot claim legacy provenance.");
        if (desired.Roles.Any(r => !r.Present && !current.Roles.Any(c => DatabaseAccessPlanner.Names.Equals(c.Name, r.Name))))
            throw new ArgumentException("Cannot drop an untracked role.");
    }

    public DatabaseAccessPreview Preview(int id)
    {
        var db = DatabaseRecord(id);
        using var context = store.Open(id);
        var state = ReadState(context, db);
        EnsureProviderMatchesDatabaseType(state.Provider, db);
        ValidateDirectory(state);
        return providers.Resolve(state.Provider).Preview(Target(db), state);
    }

    public List<string> SqlLogins(int id)
    {
        var db = DatabaseRecord(id);
        using var context = store.Open(id);
        var state = ReadState(context, db);
        EnsureProviderMatchesDatabaseType(state.Provider, db);
        return providers.Resolve(state.Provider).SqlLogins(Target(db));
    }

    public DatabaseAccessPreview Apply(int id, string token, string actor)
    {
        var db = DatabaseRecord(id);
        using var context = store.Open(id);
        var state = ReadState(context, db);
        EnsureProviderMatchesDatabaseType(state.Provider, db);
        ValidateDirectory(state);
        var target = Target(db);
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

    public List<DatabaseAccessAuditApiModel> Audit(int id)
    {
        DatabaseRecord(id);
        using var context = store.OpenReadOnly();
        return context.Set<DatabaseAccessAudit>()
            .Where(x => x.DatabaseId == id)
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Take(100)
            .Select(x => new DatabaseAccessAuditApiModel
            {
                Id = x.Id,
                CreatedUtc = x.CreatedUtc,
                Actor = x.Actor,
                Action = x.Action,
                Status = x.Status,
                Detail = x.Detail
            })
            .ToList();
    }
}

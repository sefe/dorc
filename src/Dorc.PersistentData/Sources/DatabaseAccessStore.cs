using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public sealed class DatabaseAccessStore(IDeploymentContextFactory factory)
{
    public DatabaseAccessContext Open(int databaseId)
    {
        using var legacy = factory.GetContext();
        var context = new DatabaseAccessContext(new DbContextOptionsBuilder<DatabaseAccessContext>()
            .UseSqlServer(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(legacy.Database.GetConnectionString())
            {
                Pooling = false
            }.ConnectionString).Options);
        try
        {
            context.Database.OpenConnection();
            // Session-scoped, held on this non-retrying connection across preview, audit and target execution.
            context.Database.ExecuteSqlInterpolated($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={"DatabaseAccess:" + databaseId},
                    @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0;
                IF @result < 0 THROW 51002, 'Database access is busy; retry after the current operation.', 1;
                """);
            return context;
        }
        catch { context.Dispose(); throw; }
    }

    public static DatabaseAccessState Read(DatabaseAccessContext context, int databaseId)
    {
        var configuration = context.Set<DatabaseAccessConfiguration>().SingleOrDefault(x => x.DatabaseId == databaseId);
        return new()
        {
            DatabaseId = databaseId, Provider = configuration?.Provider ?? "sql-server", Revision = configuration?.Revision ?? 0,
            Principals = context.Set<DatabaseAccessPrincipalRecord>().Where(x => x.DatabaseId == databaseId).OrderBy(x => x.Name)
                .Select(x => new DatabaseAccessPrincipal { Name = x.Name, Kind = x.Kind, LoginName = x.LoginName, DirectoryId = x.DirectoryId, Present = x.Present, LegacyUserId = x.LegacyUserId }).ToList(),
            Roles = context.Set<DatabaseAccessRoleRecord>().Where(x => x.DatabaseId == databaseId).OrderBy(x => x.Name)
                .Select(x => new DatabaseAccessRole { Name = x.Name, Present = x.Present, Managed = x.Managed }).ToList(),
            Memberships = context.Set<DatabaseAccessMembershipRecord>().Where(x => x.DatabaseId == databaseId).OrderBy(x => x.Principal).ThenBy(x => x.Role)
                .Select(x => new DatabaseAccessMembership { Principal = x.Principal, Role = x.Role, Present = x.Present }).ToList()
        };
    }

    public static DatabaseAccessState Save(DatabaseAccessContext context, DatabaseAccessState state, string actor, string action)
    {
        using var transaction = context.Database.BeginTransaction();
        var configuration = context.Set<DatabaseAccessConfiguration>().SingleOrDefault(x => x.DatabaseId == state.DatabaseId);
        if (configuration == null)
        {
            configuration = new() { DatabaseId = state.DatabaseId, Provider = state.Provider };
            context.Add(configuration);
        }
        if (configuration.Revision != state.Revision)
            throw new DbUpdateConcurrencyException("Desired state changed. Reload before saving.");
        configuration.Provider = state.Provider;
        configuration.Revision++;
        foreach (var item in state.Principals)
        {
            var row = context.Set<DatabaseAccessPrincipalRecord>().Find(state.DatabaseId, item.Name);
            if (row == null) { row = new() { DatabaseId = state.DatabaseId, Name = item.Name }; context.Add(row); }
            row.Kind = item.Kind; row.LoginName = item.LoginName; row.DirectoryId = item.DirectoryId;
            row.Present = item.Present; row.LegacyUserId = item.LegacyUserId;
        }
        foreach (var item in state.Roles)
        {
            var row = context.Set<DatabaseAccessRoleRecord>().Find(state.DatabaseId, item.Name);
            if (row == null) { row = new() { DatabaseId = state.DatabaseId, Name = item.Name }; context.Add(row); }
            row.Present = item.Present; row.Managed = item.Managed;
        }
        foreach (var item in state.Memberships)
        {
            var row = context.Set<DatabaseAccessMembershipRecord>().Find(state.DatabaseId, item.Principal, item.Role);
            if (row == null) { row = new() { DatabaseId = state.DatabaseId, Principal = item.Principal, Role = item.Role }; context.Add(row); }
            row.Present = item.Present;
        }
        Audit(context, state.DatabaseId, actor, action, "Succeeded", System.Text.Json.JsonSerializer.Serialize(state));
        transaction.Commit();
        return Read(context, state.DatabaseId);
    }

    public static DatabaseAccessAudit Audit(DatabaseAccessContext context, int databaseId, string actor, string action, string status, string detail)
    {
        var row = new DatabaseAccessAudit
        {
            DatabaseId = databaseId, CreatedUtc = DateTime.UtcNow, Actor = actor,
            Action = action, Status = status, Detail = detail
        };
        context.Add(row);
        context.SaveChanges();
        return row;
    }
}

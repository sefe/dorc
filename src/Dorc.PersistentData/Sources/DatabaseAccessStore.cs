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
        var principals = (from link in context.Set<DatabaseAccessDatabasePrincipal>().AsNoTracking()
                          join shared in context.Set<DatabaseAccessSharedPrincipal>() on link.PrincipalId equals shared.Id
                          where link.DatabaseId == databaseId
                          select new { shared.Id, shared.Name, shared.Kind, shared.LoginName, shared.DirectoryId, link.Present, link.LegacyUserId })
            .ToList();
        var roles = (from link in context.Set<DatabaseAccessDatabaseRole>().AsNoTracking()
                     join shared in context.Set<DatabaseAccessSharedRole>() on link.RoleId equals shared.Id
                     where link.DatabaseId == databaseId
                     select new { shared.Id, shared.Name, link.Managed, link.Present })
            .ToList();
        var principalNames = principals.ToDictionary(p => p.Id, p => p.Name);
        var roleNames = roles.ToDictionary(r => r.Id, r => r.Name);
        return new()
        {
            DatabaseId = databaseId, Provider = configuration?.Provider ?? "sql-server", Revision = configuration?.Revision ?? 0,
            Principals = principals.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new DatabaseAccessPrincipal { Name = x.Name, Kind = x.Kind, LoginName = x.LoginName, DirectoryId = x.DirectoryId, Present = x.Present, LegacyUserId = x.LegacyUserId }).ToList(),
            Roles = roles.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new DatabaseAccessRole { Name = x.Name, Present = x.Present, Managed = x.Managed }).ToList(),
            Memberships = context.Set<DatabaseAccessDatabaseMembership>().AsNoTracking().Where(x => x.DatabaseId == databaseId).ToList()
                .Select(x => new DatabaseAccessMembership { Principal = principalNames[x.PrincipalId], Role = roleNames[x.RoleId], Present = x.Present })
                .OrderBy(x => x.Principal, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Role, StringComparer.OrdinalIgnoreCase).ToList(),
            ExcludedLegacyUserIds = context.Set<DatabaseAccessExclusionRecord>().Where(x => x.DatabaseId == databaseId)
                .OrderBy(x => x.LegacyUserId).Select(x => x.LegacyUserId).ToList()
        };
    }

    public static DatabaseAccessState Save(DatabaseAccessContext context, DatabaseAccessState state, string actor, string action,
        string? auditDetail = null)
    {
        using var transaction = context.Database.BeginTransaction();
        var databaseId = state.DatabaseId;
        var configuration = context.Set<DatabaseAccessConfiguration>().SingleOrDefault(x => x.DatabaseId == databaseId);
        if (configuration == null)
        {
            configuration = new() { DatabaseId = databaseId, Provider = state.Provider };
            context.Add(configuration);
        }
        if (configuration.Revision != state.Revision)
            throw new DbUpdateConcurrencyException("Desired state changed. Reload before saving.");
        configuration.Provider = state.Provider;
        configuration.Revision++;
        context.SaveChanges();

        var principalIds = state.Principals.ToDictionary(p => p.Name, p => SharedPrincipalId(context, p), StringComparer.OrdinalIgnoreCase);
        var roleIds = state.Roles.ToDictionary(r => r.Name, r => SharedRoleId(context, r.Name), StringComparer.OrdinalIgnoreCase);

        // Memberships are rewritten from the desired state so a remapped principal (new shared ID) carries its roles.
        context.Set<DatabaseAccessDatabaseMembership>().Where(x => x.DatabaseId == databaseId).ExecuteDelete();
        var principalLinks = context.Set<DatabaseAccessDatabasePrincipal>().Where(x => x.DatabaseId == databaseId).ToList();
        context.RemoveRange(principalLinks.Where(x => !principalIds.ContainsValue(x.PrincipalId)));
        foreach (var item in state.Principals)
        {
            var id = principalIds[item.Name];
            var row = principalLinks.SingleOrDefault(x => x.PrincipalId == id);
            if (row == null) { row = new() { DatabaseId = databaseId, PrincipalId = id }; context.Add(row); }
            row.Present = item.Present; row.LegacyUserId = item.LegacyUserId;
        }
        var roleLinks = context.Set<DatabaseAccessDatabaseRole>().Where(x => x.DatabaseId == databaseId).ToList();
        context.RemoveRange(roleLinks.Where(x => !roleIds.ContainsValue(x.RoleId)));
        foreach (var item in state.Roles)
        {
            var id = roleIds[item.Name];
            var row = roleLinks.SingleOrDefault(x => x.RoleId == id);
            if (row == null) { row = new() { DatabaseId = databaseId, RoleId = id }; context.Add(row); }
            row.Present = item.Present; row.Managed = item.Managed;
        }
        foreach (var item in state.Memberships)
            context.Add(new DatabaseAccessDatabaseMembership
            {
                DatabaseId = databaseId, PrincipalId = principalIds[item.Principal], RoleId = roleIds[item.Role], Present = item.Present
            });
        var excluded = (state.ExcludedLegacyUserIds ?? []).ToHashSet();
        var exclusions = context.Set<DatabaseAccessExclusionRecord>().Where(x => x.DatabaseId == databaseId).ToList();
        context.RemoveRange(exclusions.Where(x => !excluded.Contains(x.LegacyUserId)));
        foreach (var id in excluded.Where(id => exclusions.All(x => x.LegacyUserId != id)))
            context.Add(new DatabaseAccessExclusionRecord { DatabaseId = databaseId, LegacyUserId = id });
        Audit(context, databaseId, actor, action, "Succeeded", auditDetail ?? System.Text.Json.JsonSerializer.Serialize(state));
        transaction.Commit();
        context.ChangeTracker.Clear();
        return Read(context, databaseId);
    }

    // UPDLOCK/HOLDLOCK serialise concurrent saves on different databases that introduce the same shared identity.
    private static int SharedPrincipalId(DatabaseAccessContext context, DatabaseAccessPrincipal principal) =>
        context.Database.SqlQuery<int>($"""
            SET NOCOUNT ON;
            DECLARE @id int = (SELECT [Id] FROM [deploy].[DatabaseAccessSharedPrincipal] WITH (UPDLOCK, HOLDLOCK)
                WHERE [Kind] = {principal.Kind} AND [Name] = {principal.Name}
                  AND EXISTS (SELECT [LoginName], [DirectoryId] INTERSECT SELECT {principal.LoginName}, {principal.DirectoryId}));
            IF @id IS NULL
            BEGIN
                INSERT [deploy].[DatabaseAccessSharedPrincipal] ([Name], [Kind], [LoginName], [DirectoryId])
                VALUES ({principal.Name}, {principal.Kind}, {principal.LoginName}, {principal.DirectoryId});
                SET @id = CAST(SCOPE_IDENTITY() AS int);
            END
            SELECT @id AS [Value];
            """).AsEnumerable().Single();

    private static int SharedRoleId(DatabaseAccessContext context, string name) =>
        context.Database.SqlQuery<int>($"""
            SET NOCOUNT ON;
            DECLARE @id int = (SELECT [Id] FROM [deploy].[DatabaseAccessSharedRole] WITH (UPDLOCK, HOLDLOCK) WHERE [Name] = {name});
            IF @id IS NULL
            BEGIN
                INSERT [deploy].[DatabaseAccessSharedRole] ([Name]) VALUES ({name});
                SET @id = CAST(SCOPE_IDENTITY() AS int);
            END
            SELECT @id AS [Value];
            """).AsEnumerable().Single();

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

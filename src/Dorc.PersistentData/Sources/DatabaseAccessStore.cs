using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public sealed class DatabaseAccessStore(IDeploymentContextFactory factory) : IDatabaseAccessReader
{
    public DatabaseAccessContext OpenReadOnly()
    {
        using var legacy = factory.GetContext();
        return new DatabaseAccessContext(new DbContextOptionsBuilder<DatabaseAccessContext>()
            .UseSqlServer(legacy.Database.GetConnectionString())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
    }

    public DatabaseAccessContext OpenWritable()
    {
        using var legacy = factory.GetContext();
        return new DatabaseAccessContext(new DbContextOptionsBuilder<DatabaseAccessContext>()
            .UseSqlServer(legacy.Database.GetConnectionString())
            .Options);
    }

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

    public IReadOnlyList<DatabaseRoleAssignment> GetRoleAssignments(int databaseId)
    {
        using var context = OpenReadOnly();
        return ReadRoleAssignments(context, databaseId);
    }

    public static IReadOnlyList<DatabaseRoleAssignment> ReadRoleAssignments(DatabaseAccessContext context, int databaseId) =>
        (from membership in context.Set<DatabaseAccessRoleMembership>().AsNoTracking()
         join principalBinding in context.Set<DatabaseAccessPrincipalBinding>().AsNoTracking()
             on new { membership.DatabaseId, membership.PrincipalId } equals new { principalBinding.DatabaseId, principalBinding.PrincipalId }
         join principal in context.Set<DatabaseAccessPrincipalCatalog>().AsNoTracking()
             on principalBinding.PrincipalId equals principal.Id
         join roleBinding in context.Set<DatabaseAccessRoleBinding>().AsNoTracking()
             on new { membership.DatabaseId, membership.RoleId } equals new { roleBinding.DatabaseId, roleBinding.RoleId }
         join role in context.Set<DatabaseAccessRoleCatalog>().AsNoTracking()
             on roleBinding.RoleId equals role.Id
         where membership.DatabaseId == databaseId
               && membership.Present
               && principalBinding.Present
               && roleBinding.Present
         select new { Principal = principal.LoginName ?? principal.Name, Role = role.Name })
        .ToList()
        .Select(x => new DatabaseRoleAssignment(x.Principal, x.Role))
        .OrderBy(x => x.Principal, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.Role, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<UserApiModel> GetNativePrincipalsForDatabases(IReadOnlyCollection<int> databaseIds)
    {
        if (databaseIds.Count == 0)
            return [];
        using var context = OpenReadOnly();
        return ReadNativePrincipalsForDatabases(context, databaseIds);
    }

    public static IReadOnlyList<UserApiModel> ReadNativePrincipalsForDatabases(
        DatabaseAccessContext context, IReadOnlyCollection<int> databaseIds) =>
        (from principalBinding in context.Set<DatabaseAccessPrincipalBinding>().AsNoTracking()
         join principal in context.Set<DatabaseAccessPrincipalCatalog>().AsNoTracking()
             on principalBinding.PrincipalId equals principal.Id
         where databaseIds.Contains(principalBinding.DatabaseId)
               && principalBinding.Present
               && principal.Kind == "Native"
         select new { principal.Id, principal.Name, principal.LoginName })
        .ToList()
        .GroupBy(x => x.LoginName!, StringComparer.OrdinalIgnoreCase)
        .Select(g =>
        {
            var first = g.OrderBy(x => x.Id).First();
            return new UserApiModel
            {
                Id = first.Id,
                LoginId = first.LoginName!,
                LanId = first.LoginName!,
                DisplayName = first.Name,
                LoginType = "Native",
                LanIdType = "USER"
            };
        })
        .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public int? FindDatabaseWithNativePrincipal(IReadOnlyCollection<int> databaseIds, string loginName)
    {
        if (databaseIds.Count == 0 || string.IsNullOrWhiteSpace(loginName))
            return null;
        using var context = OpenReadOnly();
        return FindDatabaseWithNativePrincipal(context, databaseIds, loginName);
    }

    public static int? FindDatabaseWithNativePrincipal(
        DatabaseAccessContext context, IReadOnlyCollection<int> databaseIds, string loginName) =>
        (from principalBinding in context.Set<DatabaseAccessPrincipalBinding>().AsNoTracking()
         join principal in context.Set<DatabaseAccessPrincipalCatalog>().AsNoTracking()
             on principalBinding.PrincipalId equals principal.Id
         where databaseIds.Contains(principalBinding.DatabaseId)
               && principalBinding.Present
               && principal.Kind == "Native"
         select new { principalBinding.DatabaseId, principal.Name, principal.LoginName })
        .ToList()
        .Where(x => string.Equals(x.LoginName, loginName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Name, loginName, StringComparison.OrdinalIgnoreCase))
        .Select(x => (int?)x.DatabaseId)
        .OrderBy(x => x)
        .FirstOrDefault();

    public static DatabaseAccessState Read(DatabaseAccessContext context, int databaseId, string defaultProvider = "sql-server")
    {
        var configuration = context.Set<DatabaseAccessConfiguration>().SingleOrDefault(x => x.DatabaseId == databaseId);
        var principals = (from binding in context.Set<DatabaseAccessPrincipalBinding>().AsNoTracking()
                          join catalog in context.Set<DatabaseAccessPrincipalCatalog>() on binding.PrincipalId equals catalog.Id
                          where binding.DatabaseId == databaseId
                          select new { catalog.Id, catalog.Name, catalog.Kind, catalog.LoginName, catalog.DirectoryId, binding.Present, binding.LegacyUserId })
            .ToList();
        var roles = (from binding in context.Set<DatabaseAccessRoleBinding>().AsNoTracking()
                     join catalog in context.Set<DatabaseAccessRoleCatalog>() on binding.RoleId equals catalog.Id
                     where binding.DatabaseId == databaseId
                     select new { catalog.Id, catalog.Name, binding.Managed, binding.Present })
            .ToList();
        var principalNames = principals.ToDictionary(p => p.Id, p => p.Name);
        var roleNames = roles.ToDictionary(r => r.Id, r => r.Name);
        return new()
        {
            DatabaseId = databaseId,
            Provider = configuration != null ? configuration.Provider : defaultProvider,
            Revision = configuration != null ? configuration.Revision : 0,
            Principals = principals.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new DatabaseAccessPrincipal { Name = x.Name, Kind = x.Kind, LoginName = x.LoginName, DirectoryId = x.DirectoryId, Present = x.Present, LegacyUserId = x.LegacyUserId }).ToList(),
            Roles = roles.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new DatabaseAccessRole { Name = x.Name, Present = x.Present, Managed = x.Managed }).ToList(),
            Memberships = context.Set<DatabaseAccessRoleMembership>().AsNoTracking().Where(x => x.DatabaseId == databaseId).ToList()
                .Select(x => new DatabaseAccessMembership { Principal = principalNames[x.PrincipalId], Role = roleNames[x.RoleId], Present = x.Present })
                .OrderBy(x => x.Principal, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Role, StringComparer.OrdinalIgnoreCase).ToList()
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

        var principalIds = state.Principals.ToDictionary(p => p.Name, p => PrincipalCatalogId(context, p), StringComparer.OrdinalIgnoreCase);
        var roleIds = state.Roles.ToDictionary(r => r.Name, r => RoleCatalogId(context, r.Name), StringComparer.OrdinalIgnoreCase);

        // Memberships are rewritten from the desired state so a remapped principal (new catalog ID) carries its roles.
        context.Set<DatabaseAccessRoleMembership>().Where(x => x.DatabaseId == databaseId).ExecuteDelete();
        var principalBindings = context.Set<DatabaseAccessPrincipalBinding>().Where(x => x.DatabaseId == databaseId).ToList();
        context.RemoveRange(principalBindings.Where(x => !principalIds.ContainsValue(x.PrincipalId)));
        foreach (var item in state.Principals)
        {
            var id = principalIds[item.Name];
            var row = principalBindings.SingleOrDefault(x => x.PrincipalId == id);
            if (row == null) { row = new() { DatabaseId = databaseId, PrincipalId = id }; context.Add(row); }
            row.Present = item.Present; row.LegacyUserId = item.LegacyUserId;
        }
        var roleBindings = context.Set<DatabaseAccessRoleBinding>().Where(x => x.DatabaseId == databaseId).ToList();
        context.RemoveRange(roleBindings.Where(x => !roleIds.ContainsValue(x.RoleId)));
        foreach (var item in state.Roles)
        {
            var id = roleIds[item.Name];
            var row = roleBindings.SingleOrDefault(x => x.RoleId == id);
            if (row == null) { row = new() { DatabaseId = databaseId, RoleId = id }; context.Add(row); }
            row.Present = item.Present; row.Managed = item.Managed;
        }
        foreach (var item in state.Memberships)
            context.Add(new DatabaseAccessRoleMembership
            {
                DatabaseId = databaseId, PrincipalId = principalIds[item.Principal], RoleId = roleIds[item.Role], Present = item.Present
            });
        Audit(context, databaseId, actor, action, "Succeeded", auditDetail ?? System.Text.Json.JsonSerializer.Serialize(state));
        transaction.Commit();
        context.ChangeTracker.Clear();
        return Read(context, databaseId);
    }

    // UPDLOCK/HOLDLOCK serialise concurrent saves on different databases that introduce the same identity.
    private static int PrincipalCatalogId(DatabaseAccessContext context, DatabaseAccessPrincipal principal) =>
        context.Database.SqlQuery<int>($"""
            SET NOCOUNT ON;
            DECLARE @id int = (SELECT [Id] FROM [deploy].[DatabaseAccessPrincipalCatalog] WITH (UPDLOCK, HOLDLOCK)
                WHERE [Kind] = {principal.Kind} AND [Name] = {principal.Name}
                  AND EXISTS (SELECT [LoginName], [DirectoryId] INTERSECT SELECT {principal.LoginName}, {principal.DirectoryId}));
            IF @id IS NULL
            BEGIN
                INSERT [deploy].[DatabaseAccessPrincipalCatalog] ([Name], [Kind], [LoginName], [DirectoryId])
                VALUES ({principal.Name}, {principal.Kind}, {principal.LoginName}, {principal.DirectoryId});
                SET @id = CAST(SCOPE_IDENTITY() AS int);
            END
            SELECT @id AS [Value];
            """).AsEnumerable().Single();

    private static int RoleCatalogId(DatabaseAccessContext context, string name) =>
        context.Database.SqlQuery<int>($"""
            SET NOCOUNT ON;
            DECLARE @id int = (SELECT [Id] FROM [deploy].[DatabaseAccessRoleCatalog] WITH (UPDLOCK, HOLDLOCK) WHERE [Name] = {name});
            IF @id IS NULL
            BEGIN
                INSERT [deploy].[DatabaseAccessRoleCatalog] ([Name]) VALUES ({name});
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

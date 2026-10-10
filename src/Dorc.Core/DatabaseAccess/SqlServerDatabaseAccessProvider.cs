using System.Data;
using System.Security.Principal;
using Dorc.ApiModel;
using Microsoft.Data.SqlClient;

namespace Dorc.Core.DatabaseAccess;

public sealed class SqlServerDatabaseAccessProvider : IDatabaseAccessProvider
{
    public string Name => DatabaseAccessPlanner.SqlServer;

    private static SqlConnection Connect(DatabaseAccessTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Server) || string.IsNullOrWhiteSpace(target.Database)
            || new[] { "master", "model", "msdb", "tempdb" }.Contains(target.Database, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("A non-system database and registered server are required.");
        var connection = new SqlConnection(new SqlConnectionStringBuilder
        {
            DataSource = target.Server, InitialCatalog = target.Database, IntegratedSecurity = true,
            Encrypt = true, TrustServerCertificate = false, ConnectTimeout = 15,
            ApplicationName = "DOrc.DatabaseAccess"
        }.ConnectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    public DatabaseAccessPreview Preview(DatabaseAccessTarget target, DatabaseAccessState desired)
    {
        using var connection = Connect(target);
        return Plan(target, desired, Observe(connection, null));
    }

    public DatabaseAccessPreview Apply(DatabaseAccessTarget target, DatabaseAccessState desired, string token)
    {
        using var connection = Connect(target);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using (var gate = new SqlCommand("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource=N'DOrc.DatabaseAccess', @LockMode='Exclusive',
                @LockOwner='Transaction', @LockTimeout=0;
            IF @result < 0 THROW 51000, 'Another database access reconciliation is running.', 1;
            """, connection, transaction))
            gate.ExecuteNonQuery();
        var preview = Plan(target, desired, Observe(connection, transaction));
        if (preview.Errors.Count != 0 || preview.Token != token)
            throw new DatabaseAccessConflictException("Desired or observed state changed, or the preview contains errors. Generate a fresh preview.");
        foreach (var operation in preview.Operations)
        {
            using var command = new SqlCommand(CommandText(operation), connection, transaction) { CommandTimeout = 30 };
            command.ExecuteNonQuery();
        }
        var after = Plan(target, desired, Observe(connection, transaction));
        if (after.Errors.Count != 0 || after.Operations.Count != 0)
            throw new DatabaseAccessConflictException("Reconciliation did not reach the requested state; target transaction rolled back.");
        transaction.Commit();
        return after;
    }

    private static DatabaseAccessPreview Plan(DatabaseAccessTarget target, DatabaseAccessState desired, DatabaseAccessObservation observed)
    {
        var result = DatabaseAccessPlanner.Plan(desired, observed);
        result.Token = DatabaseAccessPlanner.Fingerprint(new { target, result.Token });
        return result;
    }

    public static string CommandText(DatabaseAccessOperation operation)
    {
        static string Quote(string value)
        {
            DatabaseAccessPlanner.Identifier(value);
            return "[" + value.Replace("]", "]]") + "]";
        }
        return operation.Action switch
        {
            "CreateRole" => $"CREATE ROLE {Quote(operation.Role)} AUTHORIZATION [dbo];",
            "CreateUser" => $"CREATE USER {Quote(operation.Principal)} FOR LOGIN {Quote(operation.Login)};",
            "MapUser" => $"ALTER USER {Quote(operation.Principal)} WITH LOGIN = {Quote(operation.Login)};",
            "GrantRole" => $"ALTER ROLE {Quote(operation.Role)} ADD MEMBER {Quote(operation.Principal)};",
            "RevokeRole" => $"ALTER ROLE {Quote(operation.Role)} DROP MEMBER {Quote(operation.Principal)};",
            "DropUser" => $"DROP USER {Quote(operation.Principal)};",
            "DropRole" => $"DROP ROLE {Quote(operation.Role)};",
            _ => throw new NotSupportedException($"Unsupported operation '{operation.Action}'.")
        };
    }

    private static DatabaseAccessObservation Observe(SqlConnection connection, SqlTransaction? transaction)
    {
        var result = new DatabaseAccessObservation();
        using var command = new SqlCommand("""
            IF HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION') <> 1
                THROW 51001, 'Database access discovery requires VIEW DEFINITION.', 1;
            SELECT p.name, p.type, SUSER_SNAME(p.sid), p.sid, p.principal_id
            FROM sys.database_principals p
            WHERE p.type IN ('S','U','G','E','X') ORDER BY p.name;
            SELECT name, is_fixed_role FROM sys.database_principals WHERE type='R' ORDER BY name;
            SELECT member.name, role.name
            FROM sys.database_role_members m
            JOIN sys.database_principals member ON member.principal_id=m.member_principal_id
            JOIN sys.database_principals role ON role.principal_id=m.role_principal_id
            ORDER BY member.name, role.name;
            SELECT name, type, sid, is_disabled, principal_id FROM sys.server_principals
            WHERE type IN ('S','U','G') ORDER BY name;
            """, connection, transaction) { CommandTimeout = 30 };
        using var reader = command.ExecuteReader();
        static string Kind(string type) => type switch { "S" => "Native", "U" => "DirectoryUser", "G" => "DirectoryGroup", _ => "Unsupported" };
        static string? Sid(SqlDataReader row, int index, string type) =>
            type is "U" or "G" && !row.IsDBNull(index) ? new SecurityIdentifier((byte[])row[index], 0).Value : null;
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            result.Principals.Add(new()
            {
                Name = name, Kind = Kind(type), LoginName = reader.IsDBNull(2) ? null : reader.GetString(2),
                DirectoryId = Sid(reader, 3, type),
                Protected = reader.GetInt32(4) <= 4 || DatabaseAccessPlanner.ProtectedPrincipal(name)
            });
        }
        reader.NextResult();
        while (reader.Read()) result.Roles.Add(new() { Name = reader.GetString(0), Managed = !reader.GetBoolean(1) });
        reader.NextResult();
        while (reader.Read()) result.Memberships.Add(new() { Principal = reader.GetString(0), Role = reader.GetString(1) });
        reader.NextResult();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            result.Logins.Add(new()
            {
                Name = name, LoginName = name, Kind = Kind(reader.GetString(1)), DirectoryId = Sid(reader, 2, reader.GetString(1)),
                Protected = reader.GetBoolean(3) || reader.GetInt32(4) == 1 || DatabaseAccessPlanner.ProtectedPrincipal(name)
            });
        }
        if (result.Principals.GroupBy(p => p.Name, DatabaseAccessPlanner.Names).Any(g => g.Count() > 1)
            || result.Roles.GroupBy(r => r.Name, DatabaseAccessPlanner.Names).Any(g => g.Count() > 1)
            || result.Logins.GroupBy(l => l.Name, DatabaseAccessPlanner.Names).Any(g => g.Count() > 1))
            throw new NotSupportedException("Case-colliding target principals, roles or logins cannot be managed by this provider.");
        return result;
    }
}

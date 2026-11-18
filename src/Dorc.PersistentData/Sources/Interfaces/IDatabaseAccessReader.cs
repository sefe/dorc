using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces;

public sealed record DatabaseRoleAssignment(string Principal, string Role);

public interface IDatabaseAccessReader
{
    IReadOnlyList<DatabaseRoleAssignment> GetRoleAssignments(int databaseId);
    IReadOnlyList<UserApiModel> GetNativePrincipalsForDatabases(IReadOnlyCollection<int> databaseIds);
    int? FindDatabaseWithNativePrincipal(IReadOnlyCollection<int> databaseIds, string loginName);
}

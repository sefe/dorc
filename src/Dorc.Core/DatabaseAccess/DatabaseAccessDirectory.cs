using Dorc.ApiModel;
using Dorc.Core.Interfaces;

namespace Dorc.Core.DatabaseAccess;

public sealed class DatabaseAccessDirectory(IActiveDirectorySearcher directory)
{
    private static DatabaseAccessDirectoryIdentity Map(UserElementApiModel user)
    {
        var id = string.IsNullOrWhiteSpace(user.Pid) ? user.Sid : user.Pid;
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Directory provider did not return a stable identity.");
        return new() { Id = id, DisplayName = user.DisplayName, IsGroup = user.IsGroup };
    }

    public List<DatabaseAccessDirectoryIdentity> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3 || query.Length > 100)
            throw new ArgumentException("Directory search requires 3-100 characters.");
        return directory.Search(query).Select(Map).Take(100).ToList();
    }

    public DatabaseAccessDirectoryIdentity Resolve(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256)
            throw new ArgumentException("A stable directory identity is required.");
        var identity = Map(directory.GetUserDataById(id));
        if (!StringComparer.Ordinal.Equals(identity.Id, id))
            throw new ArgumentException("Directory returned a different identity.");
        return identity;
    }
}

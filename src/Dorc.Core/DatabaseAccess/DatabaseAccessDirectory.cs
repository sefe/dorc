using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Dorc.Core.DatabaseAccess;

public sealed class DatabaseAccessDirectory
{
    private readonly IActiveDirectorySearcher _directory;
    private readonly Func<bool> _isWindows;

    public DatabaseAccessDirectory(IActiveDirectorySearcher directory)
        : this(directory, OperatingSystem.IsWindows)
    {
    }

    internal DatabaseAccessDirectory(IActiveDirectorySearcher directory, Func<bool> isWindows)
    {
        _directory = directory;
        _isWindows = isWindows;
    }

    private DatabaseAccessDirectoryIdentity Map(UserElementApiModel user)
    {
        var id = string.IsNullOrWhiteSpace(user.Pid) ? user.Sid : user.Pid;
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Directory provider did not return a stable identity.");
        return new() { Id = id, DisplayName = user.DisplayName, IsGroup = user.IsGroup, AccountName = AccountName(id) };
    }

    // The SQL Server login for an AD identity is DOMAIN\account, which is also the conventional database user name.
    private string? AccountName(string id)
    {
        if (!_isWindows()) return null;
        return TranslateSidToAccount(id);
    }

    [SupportedOSPlatform("windows")]
    private static string? TranslateSidToAccount(string id)
    {
        try { return ((NTAccount)new SecurityIdentifier(id).Translate(typeof(NTAccount))).Value; }
        catch (SystemException) { return null; }
    }

    public List<DatabaseAccessDirectoryIdentity> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 3 || query.Length > 100)
            throw new ArgumentException("Directory search requires 3-100 characters.");
        return _directory.Search(query).Select(Map).Take(100).ToList();
    }

    public DatabaseAccessDirectoryIdentity Resolve(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256)
            throw new ArgumentException("A stable directory identity is required.");
        var identity = Map(_directory.GetUserDataById(id));
        if (!StringComparer.Ordinal.Equals(identity.Id, id))
            throw new ArgumentException("Directory returned a different identity.");
        return identity;
    }
}

using Dorc.ApiModel;
using System.DirectoryServices;

namespace Dorc.Core.Interfaces
{
    public interface IActiveDirectorySearcher
    {
        List<DirectoryEntry> Search(string objectName);
        UserApiModel GetUserByLanId(string lanId);
        ActiveDirectoryElementApiModel GetUserIdActiveDirectory(string id);
    }
}
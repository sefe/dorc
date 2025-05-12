using Dorc.ApiModel;

namespace Dorc.Core.Interfaces
{
    public interface IActiveDirectorySearcher
    {
        List<ActiveDirectoryElementApiModel> Search(string objectName);
        ActiveDirectoryElementApiModel GetUserIdActiveDirectory(string id);
    }
}
using Dorc.ApiModel;

namespace Dorc.Core.Interfaces
{
    public interface IPrincipalDirectory
    {
        List<DirectoryPrincipalApiModel> Search(string objectName);
        DirectoryPrincipalApiModel FindByName(string name);
        DirectoryPrincipalApiModel FindById(string id);
        List<string> GetIdentifiersForUser(string username);
        string? FindGroupIfMember(string userName, string groupName, string domainName);
    }
}
using Dorc.ApiModel;
using Dorc.PersistentData.Model;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IAdGroupPersistentSource
    {
        IEnumerable<GroupApiModel> GetAdGroups();
        AdGroup GetAdGroup(string name);
    }
}
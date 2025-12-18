using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.PersistentData.Sources
{
    public class AdGroupPersistentSource : IAdGroupPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public AdGroupPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public IEnumerable<GroupApiModel> GetAdGroups()
        {
            using (var context = _contextFactory.GetContext())
            {
                var result = context.AdGroups.ToList();
                return result.Select(MapToGroupApiModel).ToList();
            }
        }

        public AdGroup? GetAdGroup(string name)
        {
            using (var context = _contextFactory.GetContext())
            {
                var result = context.AdGroups
                    .FirstOrDefault(g => g.Name == name);
                return result;
            }
        }

        GroupApiModel MapToGroupApiModel(AdGroup g)
        {
            return new GroupApiModel
            {
                GroupId = g.Id,
                GroupName = g.Name
            };
        }
    }
}
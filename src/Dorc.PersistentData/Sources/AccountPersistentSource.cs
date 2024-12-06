using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class AccountPersistentSource : IAccountPersistentSource
    {
        private const string UserLanIdType = "USER";
        private const string GroupLanIdType = "GROUP";

        private readonly IDeploymentContextFactory deploymentContextFactory;

        public AccountPersistentSource(IDeploymentContextFactory deploymentContextFactory)
        {
            this.deploymentContextFactory = deploymentContextFactory;
        }

        public async Task<bool> UserExistsAsync(string lanId, string accountType)
        {
            using (var context = deploymentContextFactory.GetContext())
            {
                return await context.Users.AnyAsync(user =>
                    EF.Functions.Collate(user.LanIdType, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(UserLanIdType, DeploymentContext.CaseInsensitiveCollation)
                    && EF.Functions.Collate(user.LanId, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(lanId, DeploymentContext.CaseInsensitiveCollation)
                    && EF.Functions.Collate(user.LoginType, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(accountType, DeploymentContext.CaseInsensitiveCollation))
                    .ConfigureAwait(false);
            }
        }

        public async Task<bool> GroupExistsAsync(string lanId, string accountType)
        {
            using (var context = deploymentContextFactory.GetContext())
            {
                return await context.Users.AnyAsync(group =>
                    EF.Functions.Collate(group.LanIdType, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(GroupLanIdType, DeploymentContext.CaseInsensitiveCollation)
                    && EF.Functions.Collate(group.LanId, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(lanId, DeploymentContext.CaseInsensitiveCollation)
                    && EF.Functions.Collate(group.LoginType, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(accountType, DeploymentContext.CaseInsensitiveCollation))
                    .ConfigureAwait(false);
            }
        }
    }
}

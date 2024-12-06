using Dorc.PersistentData.Contexts;
using Microsoft.EntityFrameworkCore;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.Sources
{
    public class EnvironmentUnifier
    {
        public static Environment GetEnvironment(IDeploymentContext context, string envName)
        {
            return context.Environments.SingleOrDefault(
                x => EF.Functions.Collate(x.Name, DeploymentContext.CaseInsensitiveCollation)
                    == EF.Functions.Collate(envName, DeploymentContext.CaseInsensitiveCollation))!;
        }

        public static Environment GetEnvironment(IDeploymentContext context, int envId)
        {
            return context.Environments.Include(d => d.Databases).Include(s => s.Servers).SingleOrDefault(x => x.Id == envId);
        }
    }
}
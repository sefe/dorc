using Dorc.PersistentData.Contexts;
using Microsoft.EntityFrameworkCore;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.Sources
{
    public class EnvironmentUnifier
    {
        public static Environment? GetEnvironment(IDeploymentContext context, string envName)
        {
            return context.Environments
                .Include(e => e.AccessControls)
                .SingleOrDefault(
                x => EF.Functions.Collate(x.Name, DeploymentContext.CaseInsensitiveCollation)
                    == EF.Functions.Collate(envName, DeploymentContext.CaseInsensitiveCollation));
        }

        public static Environment? GetEnvironmentWithParentChild(IDeploymentContext context, string envName)
        {
            return context.Environments
                .Include(e => e.AccessControls)
                .Include(e => e.ParentEnvironment)
                .Include(e => e.ChildEnvironments)
                .SingleOrDefault(
                x => EF.Functions.Collate(x.Name, DeploymentContext.CaseInsensitiveCollation)
                    == EF.Functions.Collate(envName, DeploymentContext.CaseInsensitiveCollation));
        }

        public static Environment? GetEnvironment(IDeploymentContext context, int envId)
        {
            return context.Environments
                .Include(e => e.AccessControls)
                .SingleOrDefault(x => x.Id == envId);
        }

        public static Environment? GetFullEnvironment(IDeploymentContext context, int envId)
        {
            return context.Environments
                .Include(e => e.AccessControls)
                .Include(d => d.Databases)
                .Include(s => s.Servers)
                .Include(e => e.ParentEnvironment)
                .Include(e => e.ChildEnvironments)
                .SingleOrDefault(x => x.Id == envId);
        }
    }
}
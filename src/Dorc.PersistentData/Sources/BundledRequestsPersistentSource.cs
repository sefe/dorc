using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{

    public class BundledRequestsPersistentSource : IBundledRequestsPersistentSource
    {
        private readonly IDeploymentContextFactory contextFactory;
        private readonly ILog logger;

        public BundledRequestsPersistentSource(
            IDeploymentContextFactory contextFactory,
            ILog logger)
        {
            this.contextFactory = contextFactory;
            this.logger = logger;
        }

        public IEnumerable<BundledRequestsApiModel> GetRequestsForBundle(string bundleName)
        {
            using var context = contextFactory.GetContext();
            return context.BundledRequests.Where(br=>
                EF.Functions.Collate(bundleName, DeploymentContext.CaseInsensitiveCollation) ==
                EF.Functions.Collate(br.BundleName, DeploymentContext.CaseInsensitiveCollation)
                ).Select(
                requests => MapToBundledRequestApiModel(requests)).ToList();
        }

        public IEnumerable<BundledRequestsApiModel> GetBundles(string projectName)
        {
            using var context = contextFactory.GetContext();

            var project = context.Projects.FirstOrDefault(p =>
                EF.Functions.Collate(projectName, DeploymentContext.CaseInsensitiveCollation) ==
                EF.Functions.Collate(p.Name, DeploymentContext.CaseInsensitiveCollation));

            if (project != null) 
                return context.BundledRequests.Where(br=>br.ProjectId==project.Id).Select(requests =>
                    MapToBundledRequestApiModel(requests)).ToList();

            logger.Warn($"Project with name {projectName} not found");
            return new List<BundledRequestsApiModel>();
        }

        static BundledRequestsApiModel MapToBundledRequestApiModel(BundledRequests bundledRequests)
        {
            return new BundledRequestsApiModel
            {
                BundleName = bundledRequests.BundleName,
                ProjectId = bundledRequests.ProjectId,
                Type = bundledRequests.Type,
                RequestName = bundledRequests.RequestName,
                Sequence = bundledRequests.Sequence,
                Request = bundledRequests.Request
            };
        }
    }
}

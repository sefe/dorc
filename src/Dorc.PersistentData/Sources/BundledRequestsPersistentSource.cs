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
            return context.BundledRequests.Where(br =>
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
                return context.BundledRequests.Where(br => br.ProjectId == project.Id).AsNoTracking().Select(requests =>
                    MapToBundledRequestApiModel(requests)).ToList();

            logger.Warn($"Project with name {projectName} not found");
            return new List<BundledRequestsApiModel>();
        }

        public void AddRequestToBundle(BundledRequestsApiModel model)
        {
            using var context = contextFactory.GetContext();

            bool exists = context.BundledRequests.Any(br =>
                br.ProjectId == (int?)model.ProjectId &&
                EF.Functions.Collate(br.BundleName, DeploymentContext.CaseInsensitiveCollation) ==
                EF.Functions.Collate(model.BundleName, DeploymentContext.CaseInsensitiveCollation) &&
                EF.Functions.Collate(br.RequestName, DeploymentContext.CaseInsensitiveCollation) ==
                EF.Functions.Collate(model.RequestName, DeploymentContext.CaseInsensitiveCollation));

            if (exists)
            {
                logger.Warn($"A bundled request with ProjectId {model.ProjectId}, BundleName '{model.BundleName}', and RequestName '{model.RequestName}' already exists.");
                return; // Do not add the duplicate entry
            }

            var bundledRequest = new BundledRequests
            {
                BundleName = model.BundleName,
                ProjectId = (int?)model.ProjectId,
                Type = model.Type,
                RequestName = model.RequestName,
                Sequence = model.Sequence,
                Request = model.Request
            };
            context.BundledRequests.Add(bundledRequest);
            context.SaveChanges();
        }

        public void UpdateRequestInBundle(BundledRequestsApiModel model)
        {
            using var context = contextFactory.GetContext();
            var existingBundle = context.BundledRequests.FirstOrDefault(br =>br.Id == model.Id);

            if (existingBundle != null)
            {
                existingBundle.ProjectId = (int?)model.ProjectId;
                existingBundle.Type = model.Type;
                existingBundle.RequestName = model.RequestName;
                existingBundle.Sequence = model.Sequence;
                existingBundle.Request = model.Request;
                context.SaveChanges();
            }
            else
            {
                logger.Warn($"Bundle with name {model.BundleName} not found for update");
            }
        }

        public void DeleteRequestFromBundle(int id)
        {
            using var context = contextFactory.GetContext();

            // Find the bundled request by its ID
            var bundledRequest = context.BundledRequests.FirstOrDefault(br => br.Id == id);

            if (bundledRequest != null)
            {
                // Remove the bundled request from the database
                context.BundledRequests.Remove(bundledRequest);
                context.SaveChanges();
            }
            else
            {
                // Log a warning if the bundled request was not found
                logger.Warn($"Bundled request with ID {id} not found for deletion.");
            }
        }


        static BundledRequestsApiModel MapToBundledRequestApiModel(BundledRequests bundledRequests)
        {
            return new BundledRequestsApiModel
            {
                Id = bundledRequests.Id,
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

using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class CloudResourcesPersistentSource : ICloudResourcesPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public CloudResourcesPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public IEnumerable<CloudResourceApiModel> GetAll()
        {
            using var context = _contextFactory.GetContext();
            return context.CloudResources
                .Include(c => c.Environments)
                .AsNoTracking()
                .ToList()
                .Select(MapToApiModel)
                .ToList();
        }

        public CloudResourceApiModel? GetById(int id)
        {
            using var context = _contextFactory.GetContext();
            var cloudResource = context.CloudResources
                .Include(c => c.Environments)
                .AsNoTracking()
                .FirstOrDefault(c => c.Id == id);
            return cloudResource == null ? null : MapToApiModel(cloudResource);
        }

        public CloudResourceApiModel? GetByName(string name)
        {
            using var context = _contextFactory.GetContext();
            var cloudResource = context.CloudResources
                .Include(c => c.Environments)
                .AsNoTracking()
                .FirstOrDefault(c => c.Name == name);
            return cloudResource == null ? null : MapToApiModel(cloudResource);
        }

        public CloudResourceApiModel Add(CloudResourceApiModel cloudResource)
        {
            using var context = _contextFactory.GetContext();
            var entity = new CloudResource
            {
                Name = cloudResource.Name,
                Provider = cloudResource.Provider,
                ResourceType = cloudResource.ResourceType,
                ResourceIdentifier = cloudResource.ResourceIdentifier,
                Subscription = cloudResource.Subscription,
                Tags = cloudResource.Tags
            };
            context.CloudResources.Add(entity);
            context.SaveChanges();
            return MapToApiModel(entity);
        }

        public CloudResourceApiModel? Update(int id, CloudResourceApiModel cloudResource)
        {
            using var context = _contextFactory.GetContext();
            var entity = context.CloudResources
                .Include(c => c.Environments)
                .FirstOrDefault(c => c.Id == id);
            if (entity == null)
                return null;

            entity.Name = cloudResource.Name;
            entity.Provider = cloudResource.Provider;
            entity.ResourceType = cloudResource.ResourceType;
            entity.ResourceIdentifier = cloudResource.ResourceIdentifier;
            entity.Subscription = cloudResource.Subscription;
            entity.Tags = cloudResource.Tags;
            context.SaveChanges();
            return MapToApiModel(entity);
        }

        public bool Delete(int id)
        {
            using var context = _contextFactory.GetContext();
            var entity = context.CloudResources
                .Include(c => c.Environments)
                .FirstOrDefault(c => c.Id == id);
            if (entity == null)
                return false;

            foreach (var environment in entity.Environments.ToList())
                entity.Environments.Remove(environment);

            context.CloudResources.Remove(entity);
            context.SaveChanges();
            return true;
        }

        public IEnumerable<CloudResourceApiModel> GetForEnvironmentId(int environmentId)
        {
            using var context = _contextFactory.GetContext();
            return context.CloudResources
                .Include(c => c.Environments)
                .AsNoTracking()
                .Where(c => c.Environments.Any(e => e.Id == environmentId))
                .ToList()
                .Select(MapToApiModel)
                .ToList();
        }

        public IEnumerable<string> GetEnvironmentNamesForId(int id)
        {
            using var context = _contextFactory.GetContext();
            var cloudResource = context.CloudResources
                .Include(c => c.Environments)
                .AsNoTracking()
                .FirstOrDefault(c => c.Id == id);
            return cloudResource == null
                ? Enumerable.Empty<string>()
                : cloudResource.Environments.Select(e => e.Name).ToList();
        }

        public EnvironmentAttachmentOutcome AttachToEnvironment(int id, int environmentId)
        {
            using var context = _contextFactory.GetContext();
            var cloudResource = context.CloudResources
                .Include(c => c.Environments)
                .FirstOrDefault(c => c.Id == id);
            if (cloudResource == null)
                return EnvironmentAttachmentOutcome.ItemNotFound;

            var environment = context.Environments.FirstOrDefault(e => e.Id == environmentId);
            if (environment == null)
                return EnvironmentAttachmentOutcome.EnvironmentNotFound;

            if (cloudResource.Environments.Any(e => e.Id == environmentId))
                return EnvironmentAttachmentOutcome.AlreadyAttached;

            cloudResource.Environments.Add(environment);
            context.SaveChanges();
            return EnvironmentAttachmentOutcome.Attached;
        }

        public EnvironmentAttachmentOutcome DetachFromEnvironment(int id, int environmentId)
        {
            using var context = _contextFactory.GetContext();
            var cloudResource = context.CloudResources
                .Include(c => c.Environments)
                .FirstOrDefault(c => c.Id == id);
            if (cloudResource == null)
                return EnvironmentAttachmentOutcome.ItemNotFound;

            var attached = cloudResource.Environments.FirstOrDefault(e => e.Id == environmentId);
            if (attached == null)
                return EnvironmentAttachmentOutcome.NotAttached;

            cloudResource.Environments.Remove(attached);
            context.SaveChanges();
            return EnvironmentAttachmentOutcome.Detached;
        }

        private static CloudResourceApiModel MapToApiModel(CloudResource cloudResource)
        {
            return new CloudResourceApiModel
            {
                Id = cloudResource.Id,
                Name = cloudResource.Name,
                Provider = cloudResource.Provider,
                ResourceType = cloudResource.ResourceType,
                ResourceIdentifier = cloudResource.ResourceIdentifier,
                Subscription = cloudResource.Subscription,
                Tags = cloudResource.Tags,
                EnvironmentNames = cloudResource.Environments.Select(e => e.Name).ToList()
            };
        }
    }
}

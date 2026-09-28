using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using Container = Dorc.PersistentData.Model.Container;

namespace Dorc.PersistentData.Sources
{
    public class ContainersPersistentSource : IContainersPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public ContainersPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public IEnumerable<ContainerApiModel> GetAll()
        {
            using var context = _contextFactory.GetContext();
            return context.Containers
                .Include(c => c.Environments)
                .AsNoTracking()
                .ToList()
                .Select(MapToApiModel)
                .ToList();
        }

        public ContainerApiModel? GetById(int id)
        {
            using var context = _contextFactory.GetContext();
            var container = context.Containers
                .Include(c => c.Environments)
                .AsNoTracking()
                .FirstOrDefault(c => c.Id == id);
            return container == null ? null : MapToApiModel(container);
        }

        public ContainerApiModel? GetByName(string name)
        {
            using var context = _contextFactory.GetContext();
            var container = context.Containers
                .Include(c => c.Environments)
                .AsNoTracking()
                .FirstOrDefault(c => c.Name == name);
            return container == null ? null : MapToApiModel(container);
        }

        public ContainerApiModel Add(ContainerApiModel container)
        {
            using var context = _contextFactory.GetContext();
            var entity = new Container
            {
                Name = container.Name,
                Image = container.Image,
                Registry = container.Registry,
                HostServerName = container.HostServerName,
                Tags = container.Tags
            };
            context.Containers.Add(entity);
            context.SaveChanges();
            return MapToApiModel(entity);
        }

        public ContainerApiModel? Update(int id, ContainerApiModel container)
        {
            using var context = _contextFactory.GetContext();
            var entity = context.Containers
                .Include(c => c.Environments)
                .FirstOrDefault(c => c.Id == id);
            if (entity == null)
                return null;

            entity.Name = container.Name;
            entity.Image = container.Image;
            entity.Registry = container.Registry;
            entity.HostServerName = container.HostServerName;
            entity.Tags = container.Tags;
            context.SaveChanges();
            return MapToApiModel(entity);
        }

        public bool Delete(int id)
        {
            using var context = _contextFactory.GetContext();
            var entity = context.Containers
                .Include(c => c.Environments)
                .FirstOrDefault(c => c.Id == id);
            if (entity == null)
                return false;

            foreach (var environment in entity.Environments.ToList())
                entity.Environments.Remove(environment);

            context.Containers.Remove(entity);
            context.SaveChanges();
            return true;
        }

        public IEnumerable<ContainerApiModel> GetForEnvironmentId(int environmentId)
        {
            using var context = _contextFactory.GetContext();
            return context.Containers
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
            var container = context.Containers
                .Include(c => c.Environments)
                .AsNoTracking()
                .FirstOrDefault(c => c.Id == id);
            return container == null
                ? Enumerable.Empty<string>()
                : container.Environments.Select(e => e.Name).ToList();
        }

        public EnvironmentAttachmentOutcome AttachToEnvironment(int id, int environmentId)
        {
            using var context = _contextFactory.GetContext();
            var container = context.Containers
                .Include(c => c.Environments)
                .FirstOrDefault(c => c.Id == id);
            if (container == null)
                return EnvironmentAttachmentOutcome.ItemNotFound;

            var environment = context.Environments.FirstOrDefault(e => e.Id == environmentId);
            if (environment == null)
                return EnvironmentAttachmentOutcome.EnvironmentNotFound;

            if (container.Environments.Any(e => e.Id == environmentId))
                return EnvironmentAttachmentOutcome.AlreadyAttached;

            container.Environments.Add(environment);
            context.SaveChanges();
            return EnvironmentAttachmentOutcome.Attached;
        }

        public EnvironmentAttachmentOutcome DetachFromEnvironment(int id, int environmentId)
        {
            using var context = _contextFactory.GetContext();
            var container = context.Containers
                .Include(c => c.Environments)
                .FirstOrDefault(c => c.Id == id);
            if (container == null)
                return EnvironmentAttachmentOutcome.ItemNotFound;

            var attached = container.Environments.FirstOrDefault(e => e.Id == environmentId);
            if (attached == null)
                return EnvironmentAttachmentOutcome.NotAttached;

            container.Environments.Remove(attached);
            context.SaveChanges();
            return EnvironmentAttachmentOutcome.Detached;
        }

        private static ContainerApiModel MapToApiModel(Container container)
        {
            return new ContainerApiModel
            {
                Id = container.Id,
                Name = container.Name,
                Image = container.Image,
                Registry = container.Registry,
                HostServerName = container.HostServerName,
                Tags = container.Tags,
                EnvironmentNames = container.Environments.Select(e => e.Name).ToList()
            };
        }
    }
}

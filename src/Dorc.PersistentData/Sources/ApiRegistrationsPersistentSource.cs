using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class ApiRegistrationsPersistentSource : IApiRegistrationsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;

        public ApiRegistrationsPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public IEnumerable<ApiRegistrationApiModel> GetAll()
        {
            using var context = _contextFactory.GetContext();
            return context.ApiRegistrations
                .Include(a => a.Environments)
                .AsNoTracking()
                .ToList()
                .Select(MapToApiModel)
                .ToList();
        }

        public ApiRegistrationApiModel? GetById(int id)
        {
            using var context = _contextFactory.GetContext();
            var apiRegistration = context.ApiRegistrations
                .Include(a => a.Environments)
                .AsNoTracking()
                .FirstOrDefault(a => a.Id == id);
            return apiRegistration == null ? null : MapToApiModel(apiRegistration);
        }

        public ApiRegistrationApiModel? GetByName(string name)
        {
            using var context = _contextFactory.GetContext();
            var apiRegistration = context.ApiRegistrations
                .Include(a => a.Environments)
                .AsNoTracking()
                .FirstOrDefault(a => a.Name == name);
            return apiRegistration == null ? null : MapToApiModel(apiRegistration);
        }

        public ApiRegistrationApiModel Add(ApiRegistrationApiModel apiRegistration)
        {
            using var context = _contextFactory.GetContext();
            var entity = new ApiRegistration
            {
                Name = apiRegistration.Name,
                BaseUrl = apiRegistration.BaseUrl,
                Version = apiRegistration.Version,
                HealthCheckUrl = apiRegistration.HealthCheckUrl,
                Tags = apiRegistration.Tags
            };
            context.ApiRegistrations.Add(entity);
            context.SaveChanges();
            return MapToApiModel(entity);
        }

        public ApiRegistrationApiModel? Update(int id, ApiRegistrationApiModel apiRegistration)
        {
            using var context = _contextFactory.GetContext();
            var entity = context.ApiRegistrations
                .Include(a => a.Environments)
                .FirstOrDefault(a => a.Id == id);
            if (entity == null)
                return null;

            entity.Name = apiRegistration.Name;
            entity.BaseUrl = apiRegistration.BaseUrl;
            entity.Version = apiRegistration.Version;
            entity.HealthCheckUrl = apiRegistration.HealthCheckUrl;
            entity.Tags = apiRegistration.Tags;
            context.SaveChanges();
            return MapToApiModel(entity);
        }

        public bool Delete(int id)
        {
            using var context = _contextFactory.GetContext();
            var entity = context.ApiRegistrations
                .Include(a => a.Environments)
                .FirstOrDefault(a => a.Id == id);
            if (entity == null)
                return false;

            foreach (var environment in entity.Environments.ToList())
                entity.Environments.Remove(environment);

            context.ApiRegistrations.Remove(entity);
            context.SaveChanges();
            return true;
        }

        public IEnumerable<ApiRegistrationApiModel> GetForEnvironmentId(int environmentId)
        {
            using var context = _contextFactory.GetContext();
            return context.ApiRegistrations
                .Include(a => a.Environments)
                .AsNoTracking()
                .Where(a => a.Environments.Any(e => e.Id == environmentId))
                .ToList()
                .Select(MapToApiModel)
                .ToList();
        }

        public IEnumerable<string> GetEnvironmentNamesForId(int id)
        {
            using var context = _contextFactory.GetContext();
            var apiRegistration = context.ApiRegistrations
                .Include(a => a.Environments)
                .AsNoTracking()
                .FirstOrDefault(a => a.Id == id);
            return apiRegistration == null
                ? Enumerable.Empty<string>()
                : apiRegistration.Environments.Select(e => e.Name).ToList();
        }

        public EnvironmentAttachmentOutcome AttachToEnvironment(int id, int environmentId)
        {
            using var context = _contextFactory.GetContext();
            var apiRegistration = context.ApiRegistrations
                .Include(a => a.Environments)
                .FirstOrDefault(a => a.Id == id);
            if (apiRegistration == null)
                return EnvironmentAttachmentOutcome.ItemNotFound;

            var environment = context.Environments.FirstOrDefault(e => e.Id == environmentId);
            if (environment == null)
                return EnvironmentAttachmentOutcome.EnvironmentNotFound;

            if (apiRegistration.Environments.Any(e => e.Id == environmentId))
                return EnvironmentAttachmentOutcome.AlreadyAttached;

            apiRegistration.Environments.Add(environment);
            context.SaveChanges();
            return EnvironmentAttachmentOutcome.Attached;
        }

        public EnvironmentAttachmentOutcome DetachFromEnvironment(int id, int environmentId)
        {
            using var context = _contextFactory.GetContext();
            var apiRegistration = context.ApiRegistrations
                .Include(a => a.Environments)
                .FirstOrDefault(a => a.Id == id);
            if (apiRegistration == null)
                return EnvironmentAttachmentOutcome.ItemNotFound;

            var attached = apiRegistration.Environments.FirstOrDefault(e => e.Id == environmentId);
            if (attached == null)
                return EnvironmentAttachmentOutcome.NotAttached;

            apiRegistration.Environments.Remove(attached);
            context.SaveChanges();
            return EnvironmentAttachmentOutcome.Detached;
        }

        private static ApiRegistrationApiModel MapToApiModel(ApiRegistration apiRegistration)
        {
            return new ApiRegistrationApiModel
            {
                Id = apiRegistration.Id,
                Name = apiRegistration.Name,
                BaseUrl = apiRegistration.BaseUrl,
                Version = apiRegistration.Version,
                HealthCheckUrl = apiRegistration.HealthCheckUrl,
                Tags = apiRegistration.Tags,
                EnvironmentNames = apiRegistration.Environments.Select(e => e.Name).ToList()
            };
        }
    }
}

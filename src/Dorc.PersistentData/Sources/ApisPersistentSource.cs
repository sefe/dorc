using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.Sources
{
    public class ApisPersistentSource : IApisPersistentSource
    {
        private const string UpdateTypeCreate = "API Created";
        private const string UpdateTypeUpdate = "API Updated";
        private const string UpdateTypeDelete = "API Deleted";

        private readonly IDeploymentContextFactory _contextFactory;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public ApisPersistentSource(
            IDeploymentContextFactory contextFactory,
            IClaimsPrincipalReader claimsPrincipalReader)
        {
            _contextFactory = contextFactory;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public IEnumerable<ApiApiModel> GetApisForEnvId(int environmentId)
        {
            using var context = _contextFactory.GetContext();

            var env = EnvironmentUnifier.GetEnvironment(context, environmentId);
            if (env == null)
                return new List<ApiApiModel>();

            return context.Apis
                .Include(a => a.Environment)
                .Include(a => a.OwnerProject)
                .Where(a => a.EnvId == environmentId)
                .OrderBy(a => a.Name)
                .ToList()
                .Select(MapToApiModel)
                .ToList();
        }

        public ApiApiModel? GetApi(int id)
        {
            using var context = _contextFactory.GetContext();

            var api = context.Apis
                .Include(a => a.Environment)
                .Include(a => a.OwnerProject)
                .FirstOrDefault(a => a.Id == id);

            return api == null ? null : MapToApiModel(api);
        }

        public ApiApiModel AddApi(int environmentId, ApiApiModel model, IPrincipal user)
        {
            using var context = _contextFactory.GetContext();

            var env = EnvironmentUnifier.GetEnvironment(context, environmentId)
                ?? throw new ArgumentException($"Environment {environmentId} not found.");

            ValidateModel(model);

            if (context.Apis.Any(a => a.EnvId == environmentId && a.Name == model.Name))
                throw new ArgumentException($"An API named '{model.Name}' already exists for this environment.");

            var entity = new Api
            {
                EnvId = environmentId,
                Name = model.Name,
                Endpoint = model.Endpoint,
                Description = model.Description,
                Type = model.Type,
                AuthType = model.AuthType,
                HealthCheckPath = model.HealthCheckPath,
                OwnerProjectId = model.OwnerProjectId,
                Tags = model.Tags
            };

            context.Apis.Add(entity);

            string updatedBy = _claimsPrincipalReader.GetUserFullDomainName(user);
            EnvironmentHistoryPersistentSource.AddHistoryAction(
                env, string.Empty, model.Endpoint, updatedBy, UpdateTypeCreate,
                $"API '{model.Name}' added with endpoint '{model.Endpoint}'", context);

            context.SaveChanges();

            return MapToApiModel(LoadWithIncludes(context, entity.Id));
        }

        public ApiApiModel? UpdateApi(ApiApiModel model, IPrincipal user)
        {
            using var context = _contextFactory.GetContext();

            var entity = context.Apis
                .Include(a => a.Environment)
                .FirstOrDefault(a => a.Id == model.Id);

            if (entity == null)
                return null;

            ValidateModel(model);

            if (context.Apis.Any(a => a.EnvId == entity.EnvId && a.Name == model.Name && a.Id != entity.Id))
                throw new ArgumentException($"An API named '{model.Name}' already exists for this environment.");

            string oldEndpoint = entity.Endpoint;

            entity.Name = model.Name;
            entity.Endpoint = model.Endpoint;
            entity.Description = model.Description;
            entity.Type = model.Type;
            entity.AuthType = model.AuthType;
            entity.HealthCheckPath = model.HealthCheckPath;
            entity.OwnerProjectId = model.OwnerProjectId;
            entity.Tags = model.Tags;

            string updatedBy = _claimsPrincipalReader.GetUserFullDomainName(user);
            EnvironmentHistoryPersistentSource.AddHistoryAction(
                entity.Environment!, oldEndpoint, model.Endpoint, updatedBy, UpdateTypeUpdate,
                $"API '{model.Name}' updated", context);

            context.SaveChanges();

            return MapToApiModel(LoadWithIncludes(context, entity.Id));
        }

        public bool DeleteApi(int id, IPrincipal user)
        {
            using var context = _contextFactory.GetContext();

            var entity = context.Apis
                .Include(a => a.Environment)
                .FirstOrDefault(a => a.Id == id);

            if (entity == null)
                return false;

            string updatedBy = _claimsPrincipalReader.GetUserFullDomainName(user);
            EnvironmentHistoryPersistentSource.AddHistoryAction(
                entity.Environment!, entity.Endpoint, string.Empty, updatedBy, UpdateTypeDelete,
                $"API '{entity.Name}' deleted", context);

            context.Apis.Remove(entity);
            context.SaveChanges();
            return true;
        }

        private static Api LoadWithIncludes(IDeploymentContext context, int id)
        {
            return context.Apis
                .Include(a => a.Environment)
                .Include(a => a.OwnerProject)
                .First(a => a.Id == id);
        }

        private static void ValidateModel(ApiApiModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
                throw new ArgumentException("API Name is required.");
            if (string.IsNullOrWhiteSpace(model.Endpoint))
                throw new ArgumentException("API Endpoint is required.");
            if (string.IsNullOrWhiteSpace(model.Type))
                throw new ArgumentException("API Type is required.");
            if (string.IsNullOrWhiteSpace(model.AuthType))
                throw new ArgumentException("API AuthType is required.");
        }

        public static ApiApiModel MapToApiModel(Api entity)
        {
            return new ApiApiModel
            {
                Id = entity.Id,
                EnvironmentId = entity.EnvId,
                EnvironmentName = entity.Environment?.Name ?? string.Empty,
                Name = entity.Name,
                Endpoint = entity.Endpoint,
                EndpointResolved = entity.Endpoint,
                ResolutionStatus = ApiEndpointResolutionStatus.NoTokens,
                Description = entity.Description,
                Type = entity.Type,
                AuthType = entity.AuthType,
                HealthCheckPath = entity.HealthCheckPath,
                OwnerProjectId = entity.OwnerProjectId,
                OwnerProjectName = entity.OwnerProject?.Name,
                Tags = entity.Tags
            };
        }
    }
}

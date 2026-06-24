using System.Security.Principal;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Environment = Dorc.PersistentData.Model.Environment;

namespace Dorc.PersistentData.Sources
{
    public class ApisPersistentSource : IApisPersistentSource
    {
        private const string UpdateTypeCreate = "API Created";
        private const string UpdateTypeUpdate = "API Updated";
        private const string UpdateTypeDelete = "API Deleted";

        private static readonly HashSet<string> AllowedTypes = new(StringComparer.Ordinal) { "REST", "SOAP", "gRPC" };
        private static readonly HashSet<string> AllowedAuthTypes = new(StringComparer.Ordinal) { "None", "Basic", "Bearer", "OAuth" };

        // Mirror the column lengths declared in [deploy].[Api].
        private const int MaxNameLength = 128;
        private const int MaxEndpointLength = 1024;
        private const int MaxTypeLength = 16;
        private const int MaxHealthCheckPathLength = 512;
        private const int MaxTagsLength = 512;

        // Endpoint stores either a literal URL or one with $Var$ tokens that resolve at read time.
        // Restrict to the schemes any DOrc-deployed API can plausibly speak so that javascript:,
        // file:, data:, etc. cannot be persisted and later rendered into href= or fed to a future
        // resolver that might issue a request.
        private static readonly HashSet<string> AllowedEndpointSchemes = new(StringComparer.OrdinalIgnoreCase)
        {
            "http", "https", "grpc", "grpcs"
        };

        // SQL Server unique-key violation codes — used to map the unique-name TOCTOU race
        // to a clean ArgumentException ("already exists") instead of a 500.
        private const int SqlErrorUniqueIndex = 2601;
        private const int SqlErrorUniqueConstraint = 2627;

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

            SaveChangesMappingUniqueViolation(context, model.Name);

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

            SaveChangesMappingUniqueViolation(context, model.Name);

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
            if (model.Name.Length > MaxNameLength)
                throw new ArgumentException($"API Name must be {MaxNameLength} characters or fewer.");

            if (string.IsNullOrWhiteSpace(model.Endpoint))
                throw new ArgumentException("API Endpoint is required.");
            if (model.Endpoint.Length > MaxEndpointLength)
                throw new ArgumentException($"API Endpoint must be {MaxEndpointLength} characters or fewer.");
            ValidateEndpointScheme(model.Endpoint);

            if (string.IsNullOrWhiteSpace(model.Type))
                throw new ArgumentException("API Type is required.");
            if (model.Type.Length > MaxTypeLength)
                throw new ArgumentException($"API Type must be {MaxTypeLength} characters or fewer.");
            if (!AllowedTypes.Contains(model.Type))
                throw new ArgumentException($"API Type '{model.Type}' is not supported. Allowed: {string.Join(", ", AllowedTypes)}.");

            if (string.IsNullOrWhiteSpace(model.AuthType))
                throw new ArgumentException("API AuthType is required.");
            if (model.AuthType.Length > MaxTypeLength)
                throw new ArgumentException($"API AuthType must be {MaxTypeLength} characters or fewer.");
            if (!AllowedAuthTypes.Contains(model.AuthType))
                throw new ArgumentException($"API AuthType '{model.AuthType}' is not supported. Allowed: {string.Join(", ", AllowedAuthTypes)}.");

            if (model.HealthCheckPath?.Length > MaxHealthCheckPathLength)
                throw new ArgumentException($"API HealthCheckPath must be {MaxHealthCheckPathLength} characters or fewer.");
            if (model.Tags?.Length > MaxTagsLength)
                throw new ArgumentException($"API Tags must be {MaxTagsLength} characters or fewer.");
        }

        /// <summary>
        /// Reject endpoint values whose scheme is outside the allow-list. The endpoint is
        /// stored verbatim and later rendered into href=, so an unrestricted value is a
        /// stored-XSS / SSRF / phishing surface for any future consumer that doesn't add
        /// its own scheme guard. Catches both authority-form schemes (https://host/...) and
        /// opaque-form schemes (javascript:..., data:..., mailto:...) — the original
        /// `://`-only check missed the latter. Tokens like "$Host$" parse as relative, so
        /// templates such as "https://$Host$/v1" still pass.
        /// </summary>
        private static void ValidateEndpointScheme(string endpoint)
        {
            // The endpoint may contain unresolved $Var$ tokens; only the pre-token prefix
            // can carry a scheme. A leading $-token means the whole value is relative or
            // token-resolved at read time — accept.
            var tokenStart = endpoint.IndexOf('$');
            var schemeRegion = tokenStart >= 0 ? endpoint[..tokenStart] : endpoint;

            // RFC 3986: scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) ":". The single
            // colon is the canonical separator; "://" is just the most common form (with a
            // following authority component). We must check the single-colon form too.
            var colonPos = schemeRegion.IndexOf(':');
            if (colonPos <= 0)
                return; // no colon, or colon at position 0 — not a scheme prefix

            var scheme = schemeRegion[..colonPos];
            if (!IsValidSchemeChars(scheme))
                return; // pre-colon prefix is not a syntactic scheme — e.g. "foo bar:..."

            if (!AllowedEndpointSchemes.Contains(scheme))
                throw new ArgumentException(
                    $"API Endpoint scheme '{scheme}' is not supported. Allowed: {string.Join(", ", AllowedEndpointSchemes)}.");
        }

        private static bool IsValidSchemeChars(string s)
        {
            if (s.Length == 0 || !char.IsAsciiLetter(s[0]))
                return false;
            for (var i = 1; i < s.Length; i++)
            {
                var c = s[i];
                if (!char.IsAsciiLetterOrDigit(c) && c != '+' && c != '-' && c != '.')
                    return false;
            }
            return true;
        }

        private static void SaveChangesMappingUniqueViolation(IDeploymentContext context, string apiName)
        {
            try
            {
                context.SaveChanges();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Concurrent insert lost the race against the unique index. Surface as a
                // clean ArgumentException so the controller maps it to 400 with a useful
                // message, instead of leaking a 500 + DB stack trace.
                throw new ArgumentException(
                    $"An API named '{apiName}' already exists for this environment.", ex);
            }
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        {
            return ex.InnerException is SqlException sql
                   && (sql.Number == SqlErrorUniqueIndex || sql.Number == SqlErrorUniqueConstraint);
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

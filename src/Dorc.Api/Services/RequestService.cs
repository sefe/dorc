using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.Terraform.Catalog;

namespace Dorc.Api.Services
{
    public class RequestService : IRequestService
    {
        private readonly IDeployableBuildFactory _deployableBuildFactory;
        private readonly ILogger _log;
        private readonly IProjectsPersistentSource _projectsPersistentSource;
        private readonly ITemplateCatalog _catalog;
        private readonly IPropertiesPersistentSource _properties;
        private readonly IRequestsPersistentSource? _requestsPersistentSource;
        private readonly IEnvironmentsPersistentSource? _environmentsPersistentSource;

        public RequestService(ILogger<RequestService> log, IDeployableBuildFactory deployableBuildFactory,
            IProjectsPersistentSource projectsPersistentSource, ITemplateCatalog catalog,
            IPropertiesPersistentSource properties,
            IRequestsPersistentSource? requestsPersistentSource = null,
            IEnvironmentsPersistentSource? environmentsPersistentSource = null)
        {
            _projectsPersistentSource = projectsPersistentSource;
            _log = log;
            _deployableBuildFactory = deployableBuildFactory;
            _catalog = catalog;
            _properties = properties;
            _requestsPersistentSource = requestsPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
        }

        public RequestStatusDto CreateRequest(RequestDto request, ClaimsPrincipal user)
        {
            if (!string.IsNullOrWhiteSpace(request.Environment) &&
                _environmentsPersistentSource?.GetEnvironment(request.Environment) is { IsEphemeral: true, EnvironmentId: > 0 } targetEnv)
            {
                request.RequestProperties ??= new List<RequestProperty>();
                if (!request.RequestProperties.Any(p => string.Equals(
                        p.PropertyName,
                        TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    request.RequestProperties.Add(new RequestProperty
                    {
                        PropertyName = TerraformDestroyOperationConstants.EphemeralEnvironmentIdPropertyName,
                        PropertyValue = targetEnv.EnvironmentId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    });
                }
            }

            CheckRequest(ref request);
            ClassifySensitiveProperties(request);
            var build = _deployableBuildFactory.CreateInstance(request);
            if (build == null)
            {
                _log.LogError("Wrong build type for deployment request.");
                throw new WrongBuildTypeException($"Wrong build type. BuildUrl should start with 'http', 'file', or be a numeric run ID (GitHub), but got {request.BuildUrl}");
            }

            if (request.BuildNum != null && request.BuildNum.Contains(" [PINNED]"))
                request.BuildNum = request.BuildNum.Replace(" [PINNED]", "");

            var project = _projectsPersistentSource.GetProject(request.Project);
            var sourceControlType = project?.SourceControlType ?? SourceControlType.AzureDevOps;
            if (build.IsValid(new BuildDetails(request, sourceControlType)))
                return build.Process(request, user);

            _log.LogError("Build validation failed. {ValidationResult}", build.ValidationResult);
            throw new WrongBuildTypeException($"Build validation failed. {build.ValidationResult}");
        }

        private void ClassifySensitiveProperties(RequestDto request)
        {
            if (request.RequestProperties is not { Count: > 0 }) return;
            var sensitiveNames = new HashSet<string>(StringComparer.Ordinal);
            var selected = new HashSet<string>(request.Components ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var liveComponents = _projectsPersistentSource.GetComponentsForProject(request.Project)
                .Where(component => selected.Contains(component.ComponentName)).ToList();
            if (selected.Except(liveComponents.Select(component => component.ComponentName),
                    StringComparer.OrdinalIgnoreCase).Any() &&
                IsDestroyRequest(request) &&
                _environmentsPersistentSource?.GetEnvironment(request.Environment) is { } environment &&
                _requestsPersistentSource is not null)
            {
                foreach (var entry in _requestsPersistentSource.GetProvisionedComponentDefinitionsForEnvironment(environment.EnvironmentId)
                    .Where(e => string.Equals(e.Project.ProjectName, request.Project, StringComparison.OrdinalIgnoreCase) &&
                                selected.Contains(e.Component.ComponentName)))
                {
                    liveComponents.Add(entry.Component);
                }
            }
            var components = liveComponents.ToArray();
            if (selected.Except(components.Select(component => component.ComponentName),
                    StringComparer.OrdinalIgnoreCase).Any())
                throw new InvalidOperationException("A selected component is no longer available in this project.");
            foreach (var component in components.Where(component => component.TerraformSourceType == TerraformSourceType.Catalog))
            {
                if (string.IsNullOrWhiteSpace(component.TerraformTemplateName)
                    || string.IsNullOrWhiteSpace(component.TerraformTemplateVersion))
                    throw new InvalidOperationException("A selected Catalog component has no pinned template.");
                // IRequestService is a synchronous ASP.NET Core boundary; the
                // filesystem catalog completes synchronously. Keep classification
                // before every build path, not just artifact-free submissions.
                var manifest = _catalog.GetAsync(component.TerraformTemplateName, component.TerraformTemplateVersion)
                    .ConfigureAwait(false).GetAwaiter().GetResult()
                    ?? throw new InvalidOperationException("A selected Catalog template is unavailable.");
                sensitiveNames.UnionWith(manifest.Parameters.Where(parameter => parameter.Sensitive)
                    .Select(parameter => parameter.Name));
            }
            // Metadata reads must fail closed; the legacy IsPropertySecure
            // convenience method returns false when its database query fails.
            var secureProperties = _properties.GetAllProperties().Where(property => property.Secure)
                .Select(property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var property in request.RequestProperties)
            {
                var colonIndex = property.PropertyName.LastIndexOf(':');
                var bareName = colonIndex >= 0 ? property.PropertyName[(colonIndex + 1)..] : property.PropertyName;
                property.IsSensitive |= sensitiveNames.Contains(property.PropertyName)
                    || sensitiveNames.Contains(bareName)
                    || secureProperties.Contains(property.PropertyName)
                    || secureProperties.Contains(bareName);
            }
        }

        private static bool IsDestroyRequest(RequestDto request) =>
            request.RequestProperties.Any(p =>
                string.Equals(p.PropertyName, TerraformDestroyOperationConstants.PropertyName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.PropertyValue, TerraformDestroyOperationConstants.DestroyValue, StringComparison.OrdinalIgnoreCase));

        public void CheckRequest(ref RequestDto request)
        {
            if (!string.IsNullOrEmpty(request.BuildUrl)) return;
            var projectName = request.Project;
            var project = _projectsPersistentSource.GetProject(projectName);
            if (project == null)
            {
                var msg = $"Unable to locate a project with the name '{projectName}'";
                _log.LogError(msg);
                throw new Exception(msg);
            }
            request.BuildUrl = project.ArtefactsUrl.Split(';')[0];
        }
    }
}
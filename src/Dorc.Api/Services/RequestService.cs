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

        public RequestService(ILogger<RequestService> log, IDeployableBuildFactory deployableBuildFactory,
            IProjectsPersistentSource projectsPersistentSource, ITemplateCatalog catalog,
            IPropertiesPersistentSource properties)
        {
            _projectsPersistentSource = projectsPersistentSource;
            _log = log;
            _deployableBuildFactory = deployableBuildFactory;
            _catalog = catalog;
            _properties = properties;
        }

        public RequestStatusDto CreateRequest(RequestDto request, ClaimsPrincipal user)
        {
            CheckRequest(ref request);
            ClassifySensitiveProperties(request);
            var build = _deployableBuildFactory.CreateInstance(request);
            if (build == null)
            {
                _log.LogError($"Wrong build type: {request}");
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
            var components = _projectsPersistentSource.GetComponentsForProject(request.Project)
                .Where(component => selected.Contains(component.ComponentName)).ToArray();
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
                property.IsSensitive |= sensitiveNames.Contains(property.PropertyName)
                    || secureProperties.Contains(property.PropertyName);
        }

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
using System.Security.Claims;
using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Api.Services
{
    /// <summary>
    /// IDeployableBuild for Catalog-mode deploy requests.
    ///
    /// Catalog-mode deployments have no artifact URL: the runner's
    /// CatalogReferenceCodeSourceProvider resolves the source from the
    /// manifest at dispatch time, not from a pre-fetched artifact. This
    /// stub validates any well-formed Catalog request (the controller's
    /// pre-check has already verified every named component is Catalog-mode)
    /// and forwards the deploy request to the standard deploy library with
    /// an empty BuildUrl.
    /// </summary>
    public class CatalogDeployableBuild : IDeployableBuild
    {
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IDeployLibrary _deployLibrary;
        private string _validationMessage = string.Empty;

        public CatalogDeployableBuild(IDeployLibrary deployLibrary, IRequestsPersistentSource requestsPersistentSource)
        {
            _deployLibrary = deployLibrary;
            _requestsPersistentSource = requestsPersistentSource;
        }

        public bool IsValid(BuildDetails dorcBuild)
        {
            if (dorcBuild.Type != BuildType.Catalog)
            {
                _validationMessage = $"CatalogDeployableBuild requires BuildType.Catalog, got {dorcBuild.Type}.";
                return false;
            }
            return true;
        }

        public string ValidationResult => _validationMessage;

        public RequestStatusDto Process(RequestDto request, ClaimsPrincipal user)
        {
            // Defensive: a Catalog request with no components is malformed;
            // fail cleanly rather than NRE on .ToList().
            if (request.Components == null || request.Components.Count == 0)
            {
                return new RequestStatusDto { Id = 0, Status = "Catalog request has no components." };
            }

            // No artifact URL is forwarded to the deploy library. Components
            // resolve their source via Dorc.TerraformRunner's
            // CatalogReferenceCodeSourceProvider at dispatch time.
            var id = _deployLibrary.SubmitRequest(
                projectName: request.Project,
                environmentName: request.Environment,
                uri: string.Empty,
                buildDefinitionName: string.Empty,
                requestComponents: request.Components.ToList(),
                requestProperties: request.RequestProperties?.ToList() ?? new List<RequestProperty>(),
                user: user,
                isCatalog: true);

            return id <= 0
                ? new RequestStatusDto() { Id = 0, Status = "DeployLibrary has returned zero result" }
                : _requestsPersistentSource.GetRequestStatus(id);
        }
    }
}

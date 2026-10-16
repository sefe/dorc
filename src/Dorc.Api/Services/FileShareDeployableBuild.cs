using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData.Sources.Interfaces;
using System.Security.Claims;

namespace Dorc.Api.Services
{
    public class FileShareDeployableBuild : IDeployableBuild
    {
        private readonly IFileSystemHelper _helper;
        private string _validationMessage;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IDeployLibrary _deployLibrary;
        private readonly IProjectsPersistentSource _projectsPersistentSource;

        public FileShareDeployableBuild(IFileSystemHelper helper, IDeployLibrary deployLibrary,
            IRequestsPersistentSource requestsPersistentSource, IProjectsPersistentSource projectsPersistentSource)
        {
            _deployLibrary = deployLibrary;
            _requestsPersistentSource = requestsPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _helper = helper;
        }

        public bool IsValid(BuildDetails dorcBuild)
        {
            var buildTypeOk = dorcBuild.Type == BuildType.FileShareBuild;

            // TryCreate with UriKind.Absolute, rather than IsWellFormedUriString with
            // RelativeOrAbsolute followed by an absolute-only constructor. The previous pair
            // disagreed: a relative string passed the check and then threw UriFormatException
            // out of validation, which surfaced as a 500 rather than as a refusal.
            if (!Uri.TryCreate(dorcBuild.BuildUrl, UriKind.Absolute, out var uri))
            {
                _validationMessage = "File share build detected. But seems that folder doesn't exists or permissions denied";
                return false;
            }

            if (!_helper.DirectoryExists(uri.LocalPath) || !buildTypeOk)
            {
                _validationMessage = "File share build detected. But seems that folder doesn't exists or permissions denied";
                return false;
            }

            // The drop location is not merely data: it is published to deployment scripts as
            // $DropFolder$, and DOrc's own RunDeployment.ps1 dot-sources DeploymentCommon.ps1
            // from it. An unconfined drop location is therefore arbitrary code execution as
            // the deployment account, available to anyone who can submit a request. The
            // Azure DevOps build path already validates builds against the project's own
            // definitions; this applies the equivalent confinement to the file-share path.
            return IsWithinProjectArtefactRoots(dorcBuild);
        }

        private bool IsWithinProjectArtefactRoots(BuildDetails dorcBuild)
        {
            var project = _projectsPersistentSource.GetProject(dorcBuild.Project);

            if (project == null)
            {
                _validationMessage =
                    $"Unable to locate a project with the name '{dorcBuild.Project}', so the build location cannot be validated.";
                return false;
            }

            var permittedRoots = PathConfinement.SplitRoots(project.ArtefactsUrl).ToList();

            if (permittedRoots.Count == 0)
            {
                // An absent allow-list confines nothing. Admitting everything on that basis
                // would make the check vacuous for exactly the projects least configured.
                _validationMessage =
                    $"Project '{dorcBuild.Project}' has no artefacts location configured, so no build location can be validated against it.";
                return false;
            }

            if (!PathConfinement.IsWithinAny(dorcBuild.BuildUrl, permittedRoots))
            {
                _validationMessage =
                    $"The build location is outside the artefacts location configured for project '{dorcBuild.Project}'.";
                return false;
            }

            return true;
        }

        public string ValidationResult => _validationMessage;

        public RequestStatusDto Process(RequestDto request, ClaimsPrincipal user)
        {
            var sId = _deployLibrary.SubmitRequest(request.Project, request.Environment, request.BuildUrl, string.Empty,
                request.Components.ToList(), request.RequestProperties.ToList(), user);
            int id;
            try
            {
                id = Convert.ToInt32(sId);
            }
            catch (Exception e)
            {
                return new RequestStatusDto() { Id = 0, Status = e.Message };
            }

            return id <= 0
                ? new RequestStatusDto() { Id = 0, Status = "DeployLibrary has returned zero result" }
                : _requestsPersistentSource.GetRequestStatus(id);
        }
    }
}
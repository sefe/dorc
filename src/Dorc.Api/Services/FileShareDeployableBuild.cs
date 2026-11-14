using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData.Security;
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
        private readonly ISourceHostAllowList _sourceHosts;

        public FileShareDeployableBuild(
            IFileSystemHelper helper,
            IDeployLibrary deployLibrary,
            IRequestsPersistentSource requestsPersistentSource,
            IProjectsPersistentSource projectsPersistentSource,
            ISourceHostAllowList sourceHosts)
        {
            _deployLibrary = deployLibrary;
            _requestsPersistentSource = requestsPersistentSource;
            _projectsPersistentSource = projectsPersistentSource;
            _sourceHosts = sourceHosts;
            _helper = helper;
        }

        public bool IsValid(BuildDetails dorcBuild)
        {
            if (dorcBuild.Type != BuildType.FileShareBuild)
            {
                _validationMessage = "File share build detected. But seems that folder doesn't exists or permissions denied";
                return false;
            }

            if (string.IsNullOrWhiteSpace(dorcBuild.BuildUrl))
            {
                _validationMessage = "File share build detected. But seems that folder doesn't exists or permissions denied";
                return false;
            }

            var isRemote = SourceHost.IsRemoteOrUnc(dorcBuild.BuildUrl);
            if (isRemote && !SourceHost.TryExtractRemoteHost(dorcBuild.BuildUrl, out _))
            {
                _validationMessage =
                    $"Refusing to validate remote build location '{dorcBuild.BuildUrl}': remote or UNC authority could not be verified.";
                return false;
            }

            // TryCreate with UriKind.Absolute, rather than IsWellFormedUriString with
            // RelativeOrAbsolute followed by an absolute-only constructor. The previous pair
            // disagreed: a relative string passed the check and then threw UriFormatException
            // out of validation, which surfaced as a 500 rather than as a refusal.
            var normalizedBuildUrl = SourceHost.NormalizeExtendedUnc(dorcBuild.BuildUrl);
            if (!Uri.TryCreate(normalizedBuildUrl, UriKind.Absolute, out var uri))
            {
                _validationMessage = "File share build detected. But seems that folder doesn't exists or permissions denied";
                return false;
            }

            // Confine to the project's configured artefact roots and the operator's artefact
            // host allow-list BEFORE calling DirectoryExists so an attacker-chosen file:// or
            // UNC authority never triggers an outbound SMB/WebDAV NTLM/Kerberos handshake.
            if (!IsWithinProjectArtefactRoots(dorcBuild))
            {
                return false;
            }

            if (isRemote && !IsPermittedRemoteArtefactHost(dorcBuild.BuildUrl))
            {
                return false;
            }

            if (!_helper.DirectoryExists(uri.LocalPath))
            {
                _validationMessage = "File share build detected. But seems that folder doesn't exists or permissions denied";
                return false;
            }

            return true;
        }

        private bool IsPermittedRemoteArtefactHost(string buildUrl)
        {
            if (_sourceHosts.IsArtefactSourceUnconfigured)
            {
                _validationMessage =
                    $"Refusing to present default Windows credentials because no artefact host allow-list is configured ('{SourceHostAllowList.ArtefactHostsSetting}').";
                return false;
            }

            var decision = _sourceHosts.CheckArtefactSource(buildUrl);
            if (decision == null || !decision.Allowed)
            {
                _validationMessage =
                    $"Refusing to present default Windows credentials to '{buildUrl}', because {decision?.Reason ?? "host is not permitted"}";
                return false;
            }

            return true;
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
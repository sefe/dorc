using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Sources.Interfaces;
using System.Security.Claims;

namespace Dorc.Api.Services
{
    public class FileShareDeployableBuild : IDeployableBuild
    {
        private readonly IFileSystemHelper _helper;
        private string _validationMessage = string.Empty;
        private readonly IRequestsPersistentSource _requestsPersistentSource;
        private readonly IDeployLibrary _deployLibrary;

        public FileShareDeployableBuild(IFileSystemHelper helper, IDeployLibrary deployLibrary, IRequestsPersistentSource requestsPersistentSource)
        {
            _deployLibrary = deployLibrary;
            _requestsPersistentSource = requestsPersistentSource;
            _helper = helper;
        }

        public bool IsValid(BuildDetails dorcBuild)
        {
            var buildTypeOk = dorcBuild.Type == BuildType.FileShareBuild;

            if (Uri.TryCreate(dorcBuild.BuildUrl, UriKind.Absolute, out var uri))
            {
                return _helper.DirectoryExists(uri.LocalPath) && buildTypeOk;
            }
            _validationMessage = "File share build detected. But seems that folder doesn't exists or permissions denied";
            return false;
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
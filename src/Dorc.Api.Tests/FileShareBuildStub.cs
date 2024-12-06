using Dorc.Api.Interfaces;
using Dorc.Api.Model;
using Dorc.ApiModel;
using System.Security.Claims;

namespace Dorc.Api.Tests
{
    public class FileShareBuildStub : IDeployableBuild
    {
        private readonly RequestStatusDto _result;
        private readonly bool _valid;

        public FileShareBuildStub(RequestStatusDto result, bool valid)
        {
            _valid = valid;
            _result = result;
        }

        public bool IsValid(BuildDetails dorcBuild)
        {
            return _valid;
        }

        public RequestStatusDto Process(RequestDto request, ClaimsPrincipal user)
        {
            return _result;
        }

        public string ValidationResult => "some result";
    }
}

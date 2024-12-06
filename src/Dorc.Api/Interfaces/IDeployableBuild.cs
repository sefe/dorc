using Dorc.Api.Model;
using Dorc.ApiModel;
using System.Security.Claims;

namespace Dorc.Api.Interfaces
{
    public interface IDeployableBuild
    {
        bool IsValid(BuildDetails dorcBuild);
        RequestStatusDto Process(RequestDto request, ClaimsPrincipal user);
        string ValidationResult { get; }
    }
}

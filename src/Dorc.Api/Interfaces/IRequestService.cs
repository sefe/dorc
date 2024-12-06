using Dorc.ApiModel;
using System.Security.Claims;

namespace Dorc.Api.Interfaces
{
    public interface IRequestService
    {
        RequestStatusDto CreateRequest(RequestDto request, ClaimsPrincipal user);
        void CheckRequest(ref RequestDto request);
    }
}

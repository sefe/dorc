using Dorc.ApiModel;
using System.Security.Claims;

namespace Dorc.Api.Interfaces
{
    public interface IRequests
    {
        RequestStatusDto CreateRequest(RequestDto request, ClaimsPrincipal user);
        void CheckRequest(ref RequestDto request);
    }
}

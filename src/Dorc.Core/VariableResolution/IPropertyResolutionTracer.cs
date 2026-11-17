using System.Security.Claims;
using Dorc.ApiModel;

namespace Dorc.Core.VariableResolution
{
    public interface IPropertyResolutionTracer
    {
        IReadOnlyList<PropertyResolutionTraceApiModel> TraceProperties(
            PropertyResolutionPreviewRequestApiModel request,
            EnvironmentApiModel? environment,
            ClaimsPrincipal user);
    }
}

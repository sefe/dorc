using Dorc.ApiModel;
using System.Security.Claims;

namespace Dorc.Core.Interfaces
{
    public interface IDeployLibrary
    {
        int SubmitRequest(string projectName, string environmentName, string uri,
            string buildDefinitionName, List<string> requestComponents, List<RequestProperty> requestProperties,
            ClaimsPrincipal user, string? changeRequestNumber = null);

        List<int> CopyEnvBuildWithComponentIds(string sourceEnv, string targetEnv, string strProjectName,
            int[] doDeploy, ClaimsPrincipal user);
        List<int> CopyEnvBuildAllComponents(string sourceEnv, string targetEnv, string projectName,
            ClaimsPrincipal user);

        List<int> DeployCopyEnvBuildWithComponentNames(string sourceEnv, string targetEnv, string projectName,
            string components, ClaimsPrincipal user);
    }
}
using Org.OpenAPITools.Model;

namespace Dorc.Core.AzureDevOpsServer
{
    public interface IAzureDevOpsServerWebClient
    {
        List<BuildDefinitionReference> GetBuildDefinitionsForProjects(string collection, string adosProjects, string projectRegex);
        Task<List<Build>> GetBuildsFromDefinitionsAsync(string collection, List<BuildDefinitionReference> buildDefinitions, int requestSize = 10);
        List<Build> FilterBuildsByRegex(List<string> regexList, List<Build> buildsForProject);
        List<BuildArtifact> GetBuildArtifacts(string collection, string project, string buildUri);
        List<BuildArtifact> GetBuildArtifacts(string collection, string project, int buildId);
        int ExtractBuildId(string buildUri);
        Task<List<Build>> GetBuildsFromBuildNumberAsync(string collection, string buildNumber, string projectName,
            int requestSize = 10);
    }
}
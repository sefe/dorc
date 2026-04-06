using System.Text.RegularExpressions;
using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.Models;
using Microsoft.Extensions.Logging;
using Org.OpenAPITools.Model;

namespace Dorc.Core.BuildServer
{
    /// <summary>
    /// IBuildServerClient implementation that delegates to the existing Azure DevOps web client.
    /// </summary>
    public class AzureDevOpsBuildServerClient : IBuildServerClient
    {
        private readonly ILoggerFactory _loggerFactory;

        public AzureDevOpsBuildServerClient(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public IEnumerable<DeployableArtefact> GetBuildDefinitions(string serverUrl, string projectPaths, string buildRegex)
        {
            var client = CreateClient(serverUrl);
            var output = client.GetBuildDefinitionsForProjects(serverUrl, projectPaths, buildRegex);
            return output.Select(i => new DeployableArtefact
            {
                Id = i.Id.ToString(),
                Name = i.Project.Name + "; " + i.Name
            }).OrderBy(d => d.Name);
        }

        public async Task<IEnumerable<DeployableArtefact>> GetBuildsAsync(string serverUrl, string projectPaths,
            string buildRegex, string definitionName, bool filterPinnedOnly)
        {
            var client = CreateClient(serverUrl);

            var parts = definitionName.Split(';');
            var azureDevOpsProjectName = parts[0].Trim();
            var azureDevOpsBuildDefinitionName = parts[1].Trim();

            var buildDefsForProject = client.GetBuildDefinitionsForProjects(serverUrl, projectPaths, buildRegex);

            var buildDefinitionReference = buildDefsForProject.First(def =>
                def.Name.Equals(azureDevOpsBuildDefinitionName) && def.Project.Name.Equals(azureDevOpsProjectName));

            var buildsFromDefinitionsAsync = await client.GetBuildsFromDefinitionsAsync(serverUrl,
                new List<BuildDefinitionReference> { buildDefinitionReference }).ConfigureAwait(false);

            var builds = (from build in buildsFromDefinitionsAsync
                where (!filterPinnedOnly || build.KeepForever) &&
                      build.Status == Build.StatusEnum.Completed &&
                      (build.Result == Build.ResultEnum.Succeeded || build.Result == Build.ResultEnum.PartiallySucceeded)
                select new DeployableArtefact
                {
                    Id = build.Url,
                    Date = build.FinishTime,
                    Name = build.KeepForever == true ? build.BuildNumber + " [PINNED]" : build.BuildNumber
                }).ToList();

            return builds.OrderByDescending(b => b.Date);
        }

        public async Task<string> GetBuildArtifactDownloadUrlAsync(string serverUrl, string projectPaths,
            string buildRegex, string definitionName, string buildUrl)
        {
            var client = CreateClient(serverUrl);

            var parts = definitionName.Split(';');
            var azureDevOpsProjectName = parts[0].Trim();
            var azureDevOpsBuildDefinitionName = parts[1].Trim();

            var buildDefsForProject = client.GetBuildDefinitionsForProjects(serverUrl, projectPaths, buildRegex);

            var buildDefinitionReference = buildDefsForProject.First(def =>
                def.Name.Equals(azureDevOpsBuildDefinitionName) && def.Project.Name.Equals(azureDevOpsProjectName));

            var buildsFromDefinitionsAsync = await client.GetBuildsFromDefinitionsAsync(serverUrl,
                new List<BuildDefinitionReference> { buildDefinitionReference }).ConfigureAwait(false);

            var buildValue = buildsFromDefinitionsAsync.FirstOrDefault(build => build.Url.Equals(buildUrl));
            if (buildValue == null)
                throw new ApplicationException("Failed to locate the Build requested in Azure DevOps Server!");

            var tfsProjects = projectPaths.Split(';');
            foreach (var tfsProject in tfsProjects)
            {
                var artifacts = client.GetBuildArtifacts(serverUrl, tfsProject, buildUrl);
                if (artifacts.Count > 1)
                {
                    var drop = artifacts.FirstOrDefault(v => v.Name.Equals("drop"));
                    if (drop != null) return drop.Resource.DownloadUrl;
                }
                else if (artifacts.Count == 1)
                {
                    return artifacts[0].Resource.DownloadUrl;
                }
            }

            throw new ApplicationException("Failed to locate artifacts for the Build in Azure DevOps Server!");
        }

        public async Task<BuildServerBuildInfo?> ValidateBuildAsync(string serverUrl, string projectPaths,
            string buildRegex, string? buildText, string? buildNum, string? vstsUrl, bool pinnedOnly)
        {
            var client = CreateClient(serverUrl);
            var builds = new List<Build>();

            var buildDefsForProject = client.GetBuildDefinitionsForProjects(serverUrl, projectPaths, buildRegex);

            if (buildText != null && buildText.Contains(";"))
            {
                var buildDef = buildText.Split(';')[1].Trim();
                var bDef = buildDefsForProject.Where(def => buildDef.Equals(def.Name)).ToList();
                if (!bDef.Any()) return null;

                var buildsFromDefinitionsAsync = await client.GetBuildsFromDefinitionsAsync(serverUrl, bDef);
                var filtered = pinnedOnly ? buildsFromDefinitionsAsync.Where(b => b.KeepForever) : buildsFromDefinitionsAsync;
                builds.AddRange(filtered);
            }

            if (buildText != null && !buildText.Contains(";"))
            {
                var projects = projectPaths.Split(';');
                foreach (var proj in projects)
                {
                    var buildsFromBuildNumber = await client.GetBuildsFromBuildNumberAsync(serverUrl, buildText, proj);
                    var filtered = pinnedOnly ? buildsFromBuildNumber.Where(b => b.KeepForever) : buildsFromBuildNumber;
                    builds.AddRange(filtered);
                }
            }

            if (!string.IsNullOrEmpty(vstsUrl))
            {
                // Match by vstfs:// URI or by HTTP build URL
                var buildDetails = builds.FirstOrDefault(b =>
                    b.Uri.ToString().Equals(vstsUrl) || b.Url.Equals(vstsUrl));
                if (buildDetails == null) return null;
                return new BuildServerBuildInfo
                {
                    BuildUri = buildDetails.Uri.ToString(),
                    ProjectName = buildDetails.Project.Name,
                    DefinitionName = buildDetails.Definition.Name,
                    BuildId = buildDetails.Id,
                    BuildNumber = buildDetails.BuildNumber
                };
            }

            if (!builds.Any()) return null;

            foreach (var build in builds)
            {
                if (buildNum != null && buildNum.ToLower().Equals("latest"))
                {
                    return new BuildServerBuildInfo
                    {
                        BuildUri = build.Uri.ToString(),
                        ProjectName = build.Project.Name,
                        DefinitionName = build.Definition.Name,
                        BuildId = build.Id,
                        BuildNumber = build.BuildNumber
                    };
                }

                if (buildNum != null && build.BuildNumber.Trim().Equals(buildNum.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return new BuildServerBuildInfo
                    {
                        BuildUri = build.Uri.ToString(),
                        ProjectName = build.Project.Name,
                        DefinitionName = build.Definition.Name,
                        BuildId = build.Id,
                        BuildNumber = build.BuildNumber
                    };
                }
            }

            return null;
        }

        private AzureDevOpsServerWebClient CreateClient(string serverUrl)
        {
            return new AzureDevOpsServerWebClient(serverUrl,
                _loggerFactory.CreateLogger<AzureDevOpsServerWebClient>());
        }
    }
}

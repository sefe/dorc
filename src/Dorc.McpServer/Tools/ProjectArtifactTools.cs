using System.ComponentModel;
using Dorc.ApiModel;
using ModelContextProtocol.Server;

namespace Dorc.McpServer.Tools;

[McpServerToolType]
public sealed class ProjectArtifactTools
{
    private readonly IDorcApiGateway _apiGateway;

    public ProjectArtifactTools(IDorcApiGateway apiGateway)
    {
        _apiGateway = apiGateway ?? throw new ArgumentNullException(nameof(apiGateway));
    }

    [McpServerTool(Name = "dorc_list_projects", ReadOnly = true)]
    [Description("Lists all projects registered in DOrc.")]
    public Task<McpToolOutcome<List<ProjectApiModel>>> ListProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<ProjectApiModel>>(
            "RefDataProjects",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_list_project_components", ReadOnly = true)]
    [Description("Lists deployable components for a project, optionally filtered by parent component ID.")]
    public Task<McpToolOutcome<List<DeployComponentDto>>> ListProjectComponentsAsync(
        [Description("Numeric project ID.")] int projectId,
        [Description("Optional parent component ID.")] int? parentId = null,
        CancellationToken cancellationToken = default)
    {
        var path = parentId.HasValue
            ? $"Request/Components?projectId={projectId}&parentId={parentId.Value}"
            : $"Request/Components?projectId={projectId}";

        return _apiGateway.GetAsync<List<DeployComponentDto>>(path, cancellationToken);
    }

    [McpServerTool(Name = "dorc_list_build_definitions", ReadOnly = true)]
    [Description("Lists Azure DevOps build definitions associated with a project.")]
    public Task<McpToolOutcome<List<DeployArtefactDto>>> ListBuildDefinitionsAsync(
        [Description("Numeric project ID.")] int projectId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<DeployArtefactDto>>(
            $"Request/BuildDefinitions?projectId={projectId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_list_builds", ReadOnly = true)]
    [Description("Lists available builds for a project, optional target environment, and optional build definition name.")]
    public Task<McpToolOutcome<List<DeployArtefactDto>>> ListBuildsAsync(
        [Description("Numeric project ID.")] int projectId,
        [Description("Optional target environment name.")] string? environment = null,
        [Description("Optional build definition name.")] string? buildDefinitionName = null,
        CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string> { $"projectId={projectId}" };
        if (!string.IsNullOrWhiteSpace(environment))
        {
            queryParts.Add($"environment={Uri.EscapeDataString(environment.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(buildDefinitionName))
        {
            queryParts.Add($"buildDefinitionName={Uri.EscapeDataString(buildDefinitionName.Trim())}");
        }

        return _apiGateway.GetAsync<List<DeployArtefactDto>>(
            $"Request/Builds?{string.Join("&", queryParts)}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_list_bundle_components", ReadOnly = true)]
    [Description("Lists bundle components for a project build definition and build URL.")]
    public Task<McpToolOutcome<List<DeployComponentDto>>> ListBundleComponentsAsync(
        [Description("Numeric project ID.")] int projectId,
        [Description("Optional build definition name.")] string? buildDefinitionName = null,
        [Description("Optional build URL.")] string? buildUrl = null,
        CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string> { $"projectId={projectId}" };
        if (!string.IsNullOrWhiteSpace(buildDefinitionName))
        {
            queryParts.Add($"buildDefinitionName={Uri.EscapeDataString(buildDefinitionName.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(buildUrl))
        {
            queryParts.Add($"buildUrl={Uri.EscapeDataString(buildUrl.Trim())}");
        }

        return _apiGateway.GetAsync<List<DeployComponentDto>>(
            $"Request/BundleComponents?{string.Join("&", queryParts)}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_list_bundled_requests", ReadOnly = true)]
    [Description("Lists bundled request definitions for one or more projects.")]
    public Task<McpToolOutcome<List<BundledRequestsApiModel>>> ListBundledRequestsAsync(
        [Description("Project names to query.")] List<string> projectNames,
        CancellationToken cancellationToken = default)
    {
        var query = string.Join("&", (projectNames ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => $"projectNames={Uri.EscapeDataString(p.Trim())}"));

        var path = string.IsNullOrEmpty(query) ? "BundledRequests" : $"BundledRequests?{query}";
        return _apiGateway.GetAsync<List<BundledRequestsApiModel>>(path, cancellationToken);
    }

    [McpServerTool(Name = "dorc_list_requests_for_bundle", ReadOnly = true)]
    [Description("Lists template requests belonging to a specific bundle name.")]
    public Task<McpToolOutcome<List<BundledRequestsApiModel>>> ListRequestsForBundleAsync(
        [Description("Bundle name.")] string bundleName,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<BundledRequestsApiModel>>(
            $"BundledRequests/RequestsForBundle?bundleName={Uri.EscapeDataString(bundleName)}",
            cancellationToken);
    }
}

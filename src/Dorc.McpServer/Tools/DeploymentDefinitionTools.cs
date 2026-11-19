using System.ComponentModel;
using Dorc.ApiModel;
using ModelContextProtocol.Server;

namespace Dorc.McpServer.Tools;

[McpServerToolType]
public sealed class DeploymentDefinitionTools
{
    private readonly IDorcApiGateway _apiGateway;
    private readonly ProductionEnvironmentGuard _guard;

    public DeploymentDefinitionTools(IDorcApiGateway apiGateway, ProductionEnvironmentGuard guard)
    {
        _apiGateway = apiGateway ?? throw new ArgumentNullException(nameof(apiGateway));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [McpServerTool(Name = "dorc_list_deployment_definitions", ReadOnly = true)]
    [Description("Lists component deployment definitions (PowerShell scripts, Terraform sources, revisions, and permissions) with optional filtering.")]
    public Task<McpToolOutcome<GetDeploymentDefinitionsResponse>> ListDeploymentDefinitionsAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 50, max 200).")] int limit = 50,
        [Description("Optional numeric project ID filter.")] int? projectId = null,
        [Description("Optional component type filter (PowerShell or Terraform).")] ComponentType? componentType = null,
        [Description("Optional Terraform source type filter (SharedFolder, Git, or Catalog).")] TerraformSourceType? sourceType = null,
        [Description("Optional text search across project name and component name.")] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 200);
        var queryParts = new List<string>
        {
            $"page={safePage}",
            $"limit={safeLimit}"
        };

        if (projectId.HasValue)
        {
            queryParts.Add($"projectId={projectId.Value}");
        }

        if (componentType.HasValue)
        {
            queryParts.Add($"componentType={componentType.Value}");
        }

        if (sourceType.HasValue)
        {
            queryParts.Add($"sourceType={sourceType.Value}");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            queryParts.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        return _apiGateway.GetAsync<GetDeploymentDefinitionsResponse>(
            $"DeploymentDefinitions?{string.Join("&", queryParts)}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_deployment_definition_history", ReadOnly = true)]
    [Description("Gets the change history and audit events for deployment definitions, optionally filtered by project ID or component ID.")]
    public Task<McpToolOutcome<GetDeploymentDefinitionHistoryResponse>> GetDeploymentDefinitionHistoryAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 50, max 200).")] int limit = 50,
        [Description("Optional numeric project ID filter.")] int? projectId = null,
        [Description("Optional numeric component ID filter.")] int? componentId = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 200);
        var queryParts = new List<string>
        {
            $"page={safePage}",
            $"limit={safeLimit}"
        };

        if (projectId.HasValue)
        {
            queryParts.Add($"projectId={projectId.Value}");
        }

        if (componentId.HasValue)
        {
            queryParts.Add($"componentId={componentId.Value}");
        }

        return _apiGateway.GetAsync<GetDeploymentDefinitionHistoryResponse>(
            $"DeploymentDefinitions/history?{string.Join("&", queryParts)}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_search_scripts", ReadOnly = true)]
    [Description("Searches PowerShell and deployment script definitions registered in DOrc with pagination.")]
    public Task<McpToolOutcome<GetScriptsListResponseDto>> SearchScriptsAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional filter by script Name (contains match).")] string? nameFilter = null,
        [Description("Optional filter by script Path (contains match).")] string? pathFilter = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            filters.Add(new PagedDataFilter { Path = "Name", FilterValue = nameFilter.Trim() });
        }

        if (!string.IsNullOrWhiteSpace(pathFilter))
        {
            filters.Add(new PagedDataFilter { Path = "Path", FilterValue = pathFilter.Trim() });
        }

        var operators = new PagedDataOperators
        {
            Filters = filters,
            SortOrders =
            [
                new PagedDataSorting
                {
                    Path = "Name",
                    Direction = "asc"
                }
            ]
        };

        return _apiGateway.PutAsync<PagedDataOperators, GetScriptsListResponseDto>(
            $"RefDataScripts?page={safePage}&limit={safeLimit}",
            operators,
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_release_information", ReadOnly = true)]
    [Description("Gets release build and component metadata for a list of deployment request IDs.")]
    public Task<McpToolOutcome<List<ReleaseInformationApiModel>>> GetReleaseInformationAsync(
        [Description("Non-empty list of deployment request IDs.")] List<int> requestIds,
        CancellationToken cancellationToken = default)
    {
        if (requestIds is null || requestIds.Count == 0)
        {
            return Task.FromResult(McpToolOutcome.Failure<List<ReleaseInformationApiModel>>(
                "At least one deployment request ID must be provided in 'requestIds'."));
        }

        var dto = new DeploymentRequestIds
        {
            RequestIds = requestIds
        };

        return _apiGateway.PostAsync<DeploymentRequestIds, List<ReleaseInformationApiModel>>(
            "ReleaseInformation",
            dto,
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_project_environments", ReadOnly = true)]
    [Description("Lists environments mapped to a project.")]
    public Task<McpToolOutcome<TemplateApiModel<EnvironmentApiModel>>> GetProjectEnvironmentsAsync(
        [Description("Project name.")] string projectName,
        [Description("Whether to include read-only environments (default true).")] bool includeRead = true,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<TemplateApiModel<EnvironmentApiModel>>(
            $"RefDataProjectEnvironmentMappings?project={Uri.EscapeDataString(projectName.Trim())}&includeRead={includeRead.ToString().ToLowerInvariant()}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_add_project_environment_mapping", Destructive = true)]
    [Description("Maps an environment to a project. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<string>> AddProjectEnvironmentMappingAsync(
        [Description("Project name.")] string projectName,
        [Description("Environment name to map to the project.")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return McpToolOutcome.Failure<string>("projectName is required.");
        }

        var guardError = await _guard.CheckEnvironmentMutationAllowedAsync(
            "dorc_add_project_environment_mapping",
            environmentName,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<string>(guardError);
        }

        return await _apiGateway.PostWithoutResponseBodyAsync<object?>(
            $"RefDataProjectEnvironmentMappings?project={Uri.EscapeDataString(projectName.Trim())}&environment={Uri.EscapeDataString(environmentName.Trim())}",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_remove_project_environment_mapping", Destructive = true)]
    [Description("Unmaps an environment from a project. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<string>> RemoveProjectEnvironmentMappingAsync(
        [Description("Project name.")] string projectName,
        [Description("Environment name to unmap from the project.")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return McpToolOutcome.Failure<string>("projectName is required.");
        }

        var guardError = await _guard.CheckEnvironmentMutationAllowedAsync(
            "dorc_remove_project_environment_mapping",
            environmentName,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<string>(guardError);
        }

        return await _apiGateway.DeleteWithoutResponseBodyAsync(
            $"RefDataProjectEnvironmentMappings?project={Uri.EscapeDataString(projectName.Trim())}&environment={Uri.EscapeDataString(environmentName.Trim())}",
            cancellationToken).ConfigureAwait(false);
    }
}

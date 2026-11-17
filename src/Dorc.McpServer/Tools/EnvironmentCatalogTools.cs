using System.ComponentModel;
using Dorc.ApiModel;
using ModelContextProtocol.Server;

namespace Dorc.McpServer.Tools;

[McpServerToolType]
public sealed class EnvironmentCatalogTools
{
    private readonly IDorcApiGateway _apiGateway;
    private readonly ProductionEnvironmentGuard _guard;

    public EnvironmentCatalogTools(IDorcApiGateway apiGateway, ProductionEnvironmentGuard guard)
    {
        _apiGateway = apiGateway ?? throw new ArgumentNullException(nameof(apiGateway));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [McpServerTool(Name = "dorc_list_environments", ReadOnly = true)]
    [Description("Lists environments in DOrc, optionally filtered by exact environment name.")]
    public Task<McpToolOutcome<List<EnvironmentApiModel>>> ListEnvironmentsAsync(
        [Description("Optional environment name filter.")] string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var path = string.IsNullOrWhiteSpace(environmentName)
            ? "RefDataEnvironments"
            : $"RefDataEnvironments?env={Uri.EscapeDataString(environmentName.Trim())}";

        return _apiGateway.GetAsync<List<EnvironmentApiModel>>(path, cancellationToken);
    }

    [McpServerTool(Name = "dorc_list_all_environment_names", ReadOnly = true)]
    [Description("Lists all environment names registered in DOrc.")]
    public Task<McpToolOutcome<List<string>>> ListAllEnvironmentNamesAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<string>>(
            "RefDataEnvironments/GetAllEnvironmentNames",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_environment_details", ReadOnly = true)]
    [Description("Gets full content details (servers, databases, components, builds, users) for an environment ID.")]
    public Task<McpToolOutcome<EnvironmentContentApiModel>> GetEnvironmentDetailsAsync(
        [Description("Numeric environment ID.")] int environmentId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<EnvironmentContentApiModel>(
            $"RefDataEnvironmentsDetails/{environmentId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_environment_containers", ReadOnly = true)]
    [Description("Lists containers deployed to an environment by numeric environment ID.")]
    public Task<McpToolOutcome<List<ContainerApiModel>>> GetEnvironmentContainersAsync(
        [Description("Numeric environment ID.")] int environmentId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<ContainerApiModel>>(
            $"RefDataContainers/ByEnvId/{environmentId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_environment_cloud_resources", ReadOnly = true)]
    [Description("Lists cloud resources associated with an environment by numeric environment ID.")]
    public Task<McpToolOutcome<List<CloudResourceApiModel>>> GetEnvironmentCloudResourcesAsync(
        [Description("Numeric environment ID.")] int environmentId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<CloudResourceApiModel>>(
            $"RefDataCloudResources/ByEnvId/{environmentId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_environment_owner", ReadOnly = true)]
    [Description("Gets the owner of an environment by numeric environment ID.")]
    public Task<McpToolOutcome<EnvironmentOwnerApiModel>> GetEnvironmentOwnerAsync(
        [Description("Numeric environment ID.")] int environmentId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<EnvironmentOwnerApiModel>(
            $"RefDataEnvironmentsUsers/owner/{environmentId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_environment_history", ReadOnly = true)]
    [Description("Gets the change history for an environment by numeric environment ID.")]
    public Task<McpToolOutcome<List<EnvironmentHistoryApiModel>>> GetEnvironmentHistoryAsync(
        [Description("Numeric environment ID.")] int environmentId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<EnvironmentHistoryApiModel>>(
            $"RefDataEnvironmentsHistory?id={environmentId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_copy_environment_build", Destructive = true)]
    [Description("Copies build assignments from a source environment to a target environment. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<CopyEnvBuildResponseDto>> CopyEnvironmentBuildAsync(
        [Description("Source environment name.")] string sourceEnvironmentName,
        [Description("Target environment name.")] string targetEnvironmentName,
        [Description("Project name.")] string projectName,
        [Description("Optional semicolon-separated component names to copy; when empty, copies all components.")] string? components = null,
        CancellationToken cancellationToken = default)
    {
        var guardError = await _guard.CheckEnvironmentMutationAllowedAsync(
            "dorc_copy_environment_build",
            targetEnvironmentName,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<CopyEnvBuildResponseDto>(guardError);
        }

        var dto = new CopyEnvBuildDto
        {
            SourceEnv = sourceEnvironmentName,
            TargetEnv = targetEnvironmentName,
            Project = projectName,
            Components = components ?? string.Empty
        };

        return await _apiGateway.PostAsync<CopyEnvBuildDto, CopyEnvBuildResponseDto>(
            "CopyEnvBuild",
            dto,
            cancellationToken).ConfigureAwait(false);
    }
}

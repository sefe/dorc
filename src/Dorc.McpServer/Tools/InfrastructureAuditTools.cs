using System.ComponentModel;
using Dorc.ApiModel;
using ModelContextProtocol.Server;

namespace Dorc.McpServer.Tools;

[McpServerToolType]
public sealed class InfrastructureAuditTools
{
    private readonly IDorcApiGateway _apiGateway;

    public InfrastructureAuditTools(IDorcApiGateway apiGateway)
    {
        _apiGateway = apiGateway ?? throw new ArgumentNullException(nameof(apiGateway));
    }

    [McpServerTool(Name = "dorc_search_scripts_audit", ReadOnly = true)]
    [Description("Searches the deployment script change audit log with pagination.")]
    public Task<McpToolOutcome<GetScriptsAuditListResponseDto>> SearchScriptsAuditAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional filter by ScriptName (contains match).")] string? scriptName = null,
        [Description("Optional filter by UpdatedBy user (contains match).")] string? updatedBy = null,
        [Description("Optional filter by ProjectNames (contains match).")] string? projectNames = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        AddFilterIfPresent(filters, "ScriptName", scriptName);
        AddFilterIfPresent(filters, "UpdatedBy", updatedBy);
        AddFilterIfPresent(filters, "ProjectNames", projectNames);

        var operators = CreateOperators(filters, "UpdatedDate");
        return _apiGateway.PutAsync<PagedDataOperators, GetScriptsAuditListResponseDto>(
            $"ScriptsAudit?page={safePage}&limit={safeLimit}&useAndLogic=true",
            operators,
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_search_project_audit", ReadOnly = true)]
    [Description("Searches the project reference-data change audit log, optionally scoped to a specific project ID.")]
    public Task<McpToolOutcome<GetRefDataAuditListResponseDto>> SearchProjectAuditAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional numeric project ID filter.")] int? projectId = null,
        [Description("Optional filter by Username (contains match).")] string? username = null,
        [Description("Optional filter by Action (Create, Update, Delete).")] string? action = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        AddFilterIfPresent(filters, "Username", username);
        AddFilterIfPresent(filters, "Action", action);

        var path = projectId.HasValue
            ? $"RefDataProjectAudit?projectId={projectId.Value}&page={safePage}&limit={safeLimit}"
            : $"RefDataProjectAudit?page={safePage}&limit={safeLimit}";

        return _apiGateway.PutAsync<PagedDataOperators, GetRefDataAuditListResponseDto>(
            path,
            CreateOperators(filters, "Date"),
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_daemon_observations", ReadOnly = true)]
    [Description("Gets paged runtime observation history for a daemon, optionally filtered by server ID.")]
    public Task<McpToolOutcome<GetDaemonObservationListResponseDto>> GetDaemonObservationsAsync(
        [Description("Numeric daemon ID.")] int daemonId,
        [Description("Optional numeric server ID filter.")] int? serverId = null,
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 50, max 100).")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var path = serverId.HasValue
            ? $"DaemonObservation?daemonId={daemonId}&serverId={serverId.Value}&page={safePage}&limit={safeLimit}"
            : $"DaemonObservation?daemonId={daemonId}&page={safePage}&limit={safeLimit}";

        return _apiGateway.GetAsync<GetDaemonObservationListResponseDto>(path, cancellationToken);
    }

    [McpServerTool(Name = "dorc_search_daemon_audit", ReadOnly = true)]
    [Description("Searches the daemon configuration change audit log, optionally scoped to a specific daemon ID.")]
    public Task<McpToolOutcome<GetDaemonAuditListResponseDto>> SearchDaemonAuditAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional numeric daemon ID filter.")] int? daemonId = null,
        [Description("Optional filter by DaemonName (contains match).")] string? daemonName = null,
        [Description("Optional filter by Username (contains match).")] string? username = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        AddFilterIfPresent(filters, "DaemonName", daemonName);
        AddFilterIfPresent(filters, "Username", username);

        var path = daemonId.HasValue
            ? $"DaemonAudit?daemonId={daemonId.Value}&page={safePage}&limit={safeLimit}"
            : $"DaemonAudit?page={safePage}&limit={safeLimit}";

        return _apiGateway.PutAsync<PagedDataOperators, GetDaemonAuditListResponseDto>(
            path,
            CreateOperators(filters, "Date"),
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_search_server_audit", ReadOnly = true)]
    [Description("Searches the server reference-data change audit log with pagination.")]
    public Task<McpToolOutcome<GetServerAuditListResponseDto>> SearchServerAuditAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional filter by ServerName (contains match).")] string? serverName = null,
        [Description("Optional filter by Username (contains match).")] string? username = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        AddFilterIfPresent(filters, "ServerName", serverName);
        AddFilterIfPresent(filters, "Username", username);

        return _apiGateway.PutAsync<PagedDataOperators, GetServerAuditListResponseDto>(
            $"ServerAudit?page={safePage}&limit={safeLimit}",
            CreateOperators(filters, "Date"),
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_search_database_audit", ReadOnly = true)]
    [Description("Searches the database reference-data change audit log with pagination.")]
    public Task<McpToolOutcome<GetDatabaseAuditListResponseDto>> SearchDatabaseAuditAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional filter by DatabaseName (contains match).")] string? databaseName = null,
        [Description("Optional filter by Username (contains match).")] string? username = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        AddFilterIfPresent(filters, "DatabaseName", databaseName);
        AddFilterIfPresent(filters, "Username", username);

        return _apiGateway.PutAsync<PagedDataOperators, GetDatabaseAuditListResponseDto>(
            $"DatabaseAudit?page={safePage}&limit={safeLimit}",
            CreateOperators(filters, "Date"),
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_search_container_audit", ReadOnly = true)]
    [Description("Searches the container reference-data change audit log, optionally scoped to a specific container ID.")]
    public Task<McpToolOutcome<GetComponentAuditListResponseDto>> SearchContainerAuditAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional numeric container ID filter.")] int? containerId = null,
        [Description("Optional filter by ComponentName (contains match).")] string? componentName = null,
        [Description("Optional filter by Username (contains match).")] string? username = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        AddFilterIfPresent(filters, "ComponentName", componentName);
        AddFilterIfPresent(filters, "Username", username);

        var path = containerId.HasValue
            ? $"ContainerAudit?containerId={containerId.Value}&page={safePage}&limit={safeLimit}"
            : $"ContainerAudit?page={safePage}&limit={safeLimit}";

        return _apiGateway.PutAsync<PagedDataOperators, GetComponentAuditListResponseDto>(
            path,
            CreateOperators(filters, "Date"),
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_search_cloud_resource_audit", ReadOnly = true)]
    [Description("Searches the cloud resource change audit log, optionally scoped to a specific cloud resource ID.")]
    public Task<McpToolOutcome<GetComponentAuditListResponseDto>> SearchCloudResourceAuditAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional numeric cloud resource ID filter.")] int? cloudResourceId = null,
        [Description("Optional filter by ComponentName (contains match).")] string? componentName = null,
        [Description("Optional filter by Username (contains match).")] string? username = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        AddFilterIfPresent(filters, "ComponentName", componentName);
        AddFilterIfPresent(filters, "Username", username);

        var path = cloudResourceId.HasValue
            ? $"CloudResourceAudit?cloudResourceId={cloudResourceId.Value}&page={safePage}&limit={safeLimit}"
            : $"CloudResourceAudit?page={safePage}&limit={safeLimit}";

        return _apiGateway.PutAsync<PagedDataOperators, GetComponentAuditListResponseDto>(
            path,
            CreateOperators(filters, "Date"),
            cancellationToken);
    }

    private static void AddFilterIfPresent(List<PagedDataFilter> filters, string path, string? filterValue)
    {
        if (!string.IsNullOrWhiteSpace(filterValue))
        {
            filters.Add(new PagedDataFilter
            {
                Path = path,
                FilterValue = filterValue.Trim()
            });
        }
    }

    private static PagedDataOperators CreateOperators(List<PagedDataFilter> filters, string sortPath)
    {
        return new PagedDataOperators
        {
            Filters = filters,
            SortOrders =
            [
                new PagedDataSorting
                {
                    Path = sortPath,
                    Direction = "desc"
                }
            ]
        };
    }
}

using System.ComponentModel;
using Dorc.ApiModel;
using ModelContextProtocol.Server;

namespace Dorc.McpServer.Tools;

[McpServerToolType]
public sealed class InfrastructureTopologyTools
{
    private readonly IDorcApiGateway _apiGateway;
    private readonly ProductionEnvironmentGuard _guard;

    public InfrastructureTopologyTools(IDorcApiGateway apiGateway, ProductionEnvironmentGuard guard)
    {
        _apiGateway = apiGateway ?? throw new ArgumentNullException(nameof(apiGateway));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [McpServerTool(Name = "dorc_get_access_control", ReadOnly = true)]
    [Description("Gets the access control rules (ACLs) for a secured Project (type 0) or Environment (type 1) by name.")]
    public Task<McpToolOutcome<AccessSecureApiModel>> GetAccessControlAsync(
        [Description("Access control type: Project or Environment.")] AccessControlType accessControlType,
        [Description("Name of the Project or Environment.")] string accessControlName,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<AccessSecureApiModel>(
            $"AccessControl?accessControlType={accessControlType}&accessControlName={Uri.EscapeDataString(accessControlName.Trim())}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_search_access_control_identities", ReadOnly = true)]
    [Description("Searches directory users and groups that can be assigned to DOrc access control rules.")]
    public Task<McpToolOutcome<List<UserElementApiModel>>> SearchAccessControlIdentitiesAsync(
        [Description("Search string (display name, username, or group name).")] string search,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<UserElementApiModel>>(
            $"AccessControl/SearchUsers?search={Uri.EscapeDataString(search.Trim())}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_update_access_control", Destructive = true)]
    [Description("Updates the access control rules (ACLs) for a secured Project or Environment. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<AccessSecureApiModel>> UpdateAccessControlAsync(
        [Description("Updated access control object definition including Privileges.")] AccessSecureApiModel accessControl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessControl);

        var readOnlyError = _guard.CheckMutationAllowed("dorc_update_access_control");
        if (readOnlyError is not null)
        {
            return McpToolOutcome.Failure<AccessSecureApiModel>(readOnlyError);
        }

        if (!_guard.AllowProd)
        {
            return McpToolOutcome.Failure<AccessSecureApiModel>(
                "Operation 'dorc_update_access_control' is blocked because modifying RBAC access control rules requires DORC_MCP_ALLOW_PROD=true.");
        }

        return await _apiGateway.PutAsync<AccessSecureApiModel, AccessSecureApiModel>(
            "AccessControl",
            accessControl,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_list_user_permissions", ReadOnly = true)]
    [Description("Lists all permission definitions available in DOrc.")]
    public Task<McpToolOutcome<List<PermissionDto>>> ListUserPermissionsAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<PermissionDto>>("RefDataUserPermissions", cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_server", ReadOnly = true)]
    [Description("Gets details of a registered application server by server name.")]
    public Task<McpToolOutcome<ServerApiModel>> GetServerAsync(
        [Description("Server name.")] string serverName,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<ServerApiModel>(
            $"RefDataServers/{Uri.EscapeDataString(serverName.Trim())}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_search_servers", ReadOnly = true)]
    [Description("Searches registered application servers with pagination and optional name/OS filters.")]
    public Task<McpToolOutcome<GetServerApiModelListResponseDto>> SearchServersAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional filter by server Name (contains match).")] string? nameFilter = null,
        [Description("Optional filter by OsName (contains match).")] string? osFilter = null,
        [Description("Optional filter by ApplicationTags (contains match).")] string? applicationTagsFilter = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        AddFilterIfPresent(filters, "Name", nameFilter);
        AddFilterIfPresent(filters, "OsName", osFilter);
        AddFilterIfPresent(filters, "ApplicationTags", applicationTagsFilter);

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

        return _apiGateway.PutAsync<PagedDataOperators, GetServerApiModelListResponseDto>(
            $"RefDataServers/ByPage?page={safePage}&limit={safeLimit}",
            operators,
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_list_databases", ReadOnly = true)]
    [Description("Lists registered databases, optionally filtered by database name and/or database type.")]
    public Task<McpToolOutcome<List<DatabaseApiModel>>> ListDatabasesAsync(
        [Description("Optional database name filter.")] string? name = null,
        [Description("Optional database type / application tag filter.")] string? type = null,
        CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            queryParts.Add($"name={Uri.EscapeDataString(name.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            queryParts.Add($"type={Uri.EscapeDataString(type.Trim())}");
        }

        var path = queryParts.Count == 0
            ? "RefDataDatabases"
            : $"RefDataDatabases?{string.Join("&", queryParts)}";

        return _apiGateway.GetAsync<List<DatabaseApiModel>>(path, cancellationToken);
    }

    [McpServerTool(Name = "dorc_search_databases", ReadOnly = true)]
    [Description("Searches registered databases with pagination and optional server/name/type filters.")]
    public Task<McpToolOutcome<GetDatabaseApiModelListResponseDto>> SearchDatabasesAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional filter by database Name (contains match).")] string? nameFilter = null,
        [Description("Optional filter by ServerName (contains match).")] string? serverNameFilter = null,
        [Description("Optional filter by database Type (contains match).")] string? typeFilter = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        AddFilterIfPresent(filters, "Name", nameFilter);
        AddFilterIfPresent(filters, "ServerName", serverNameFilter);
        AddFilterIfPresent(filters, "Type", typeFilter);

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

        return _apiGateway.PutAsync<PagedDataOperators, GetDatabaseApiModelListResponseDto>(
            $"RefDataDatabases/ByPage?page={safePage}&limit={safeLimit}",
            operators,
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_list_daemons", ReadOnly = true)]
    [Description("Lists all daemon definitions registered in DOrc reference data.")]
    public Task<McpToolOutcome<List<DaemonApiModel>>> ListDaemonsAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<DaemonApiModel>>("RefDataDaemons", cancellationToken);
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
}

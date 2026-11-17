using System.ComponentModel;
using Dorc.ApiModel;
using ModelContextProtocol.Server;

namespace Dorc.McpServer.Tools;

[McpServerToolType]
public sealed class PropertyResolutionTools
{
    private readonly IDorcApiGateway _apiGateway;

    public PropertyResolutionTools(IDorcApiGateway apiGateway)
    {
        _apiGateway = apiGateway ?? throw new ArgumentNullException(nameof(apiGateway));
    }

    [McpServerTool(Name = "dorc_get_property_values", ReadOnly = true)]
    [Description("Gets property values filtered by property name and/or environment name. Secure property values are automatically redacted.")]
    public async Task<McpToolOutcome<List<PropertyValueDto>>> GetPropertyValuesAsync(
        [Description("Optional property name filter.")] string? propertyName = null,
        [Description("Optional environment name filter.")] string? environmentName = null,
        CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            queryParts.Add($"propertyName={Uri.EscapeDataString(propertyName.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            queryParts.Add($"environmentName={Uri.EscapeDataString(environmentName.Trim())}");
        }

        var path = queryParts.Count == 0
            ? "PropertyValues"
            : $"PropertyValues?{string.Join("&", queryParts)}";

        var outcome = await _apiGateway.GetAsync<List<PropertyValueDto>>(path, cancellationToken).ConfigureAwait(false);
        if (!outcome.Succeeded)
        {
            return outcome;
        }

        return McpToolOutcome.Success(SecretRedactor.RedactPropertyValues(outcome.Data), outcome.HttpStatusCode ?? 200);
    }

    [McpServerTool(Name = "dorc_search_property_values", ReadOnly = true)]
    [Description("Searches scoped property values with pagination. Secure property values are automatically redacted.")]
    public async Task<McpToolOutcome<GetScopedPropertyValuesResponseDto>> SearchPropertyValuesAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional filter by Property name (contains match).")] string? propertyFilter = null,
        [Description("Optional filter by PropertyValueScope / environment name (contains match).")] string? scopeFilter = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        if (!string.IsNullOrWhiteSpace(propertyFilter))
        {
            filters.Add(new PagedDataFilter { Path = "Property", FilterValue = propertyFilter.Trim() });
        }

        if (!string.IsNullOrWhiteSpace(scopeFilter))
        {
            filters.Add(new PagedDataFilter { Path = "PropertyValueScope", FilterValue = scopeFilter.Trim() });
        }

        var operators = new PagedDataOperators
        {
            Filters = filters,
            SortOrders =
            [
                new PagedDataSorting
                {
                    Path = "Property",
                    Direction = "asc"
                }
            ]
        };

        var outcome = await _apiGateway.PutAsync<PagedDataOperators, GetScopedPropertyValuesResponseDto>(
            $"RefDataSearchPropertyValues?page={safePage}&limit={safeLimit}",
            operators,
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            return outcome;
        }

        return McpToolOutcome.Success(SecretRedactor.RedactScopedPropertyValues(outcome.Data), outcome.HttpStatusCode ?? 200);
    }

    [McpServerTool(Name = "dorc_get_property_scope_options", ReadOnly = true)]
    [Description("Gets available variable scope options for an environment. SampleResolvedValue is redacted by default.")]
    public async Task<McpToolOutcome<List<PropertyValueScopeOptionApiModel>>> GetPropertyScopeOptionsAsync(
        [Description("Environment name (propertyValueScope).")] string propertyValueScope,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _apiGateway.GetAsync<List<PropertyValueScopeOptionApiModel>>(
            $"PropertyValues/ScopeOptions?propertyValueScope={Uri.EscapeDataString(propertyValueScope)}",
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            return outcome;
        }

        return McpToolOutcome.Success(SecretRedactor.RedactScopeOptions(outcome.Data), outcome.HttpStatusCode ?? 200);
    }

    [McpServerTool(Name = "dorc_search_property_values_audit", ReadOnly = true)]
    [Description("Searches the property value change audit log. Historical FromValue and ToValue fields are redacted to prevent secret leakage.")]
    public async Task<McpToolOutcome<GetPropertyValuesAuditListResponseDto>> SearchPropertyValuesAuditAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional filter by PropertyName.")] string? propertyName = null,
        [Description("Optional filter by EnvironmentName.")] string? environmentName = null,
        [Description("Optional filter by UpdatedBy user.")] string? updatedBy = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        if (!string.IsNullOrWhiteSpace(propertyName))
        {
            filters.Add(new PagedDataFilter { Path = "PropertyName", FilterValue = propertyName.Trim() });
        }

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            filters.Add(new PagedDataFilter { Path = "EnvironmentName", FilterValue = environmentName.Trim() });
        }

        if (!string.IsNullOrWhiteSpace(updatedBy))
        {
            filters.Add(new PagedDataFilter { Path = "UpdatedBy", FilterValue = updatedBy.Trim() });
        }

        var operators = new PagedDataOperators
        {
            Filters = filters,
            SortOrders =
            [
                new PagedDataSorting
                {
                    Path = "UpdatedDate",
                    Direction = "desc"
                }
            ]
        };

        var outcome = await _apiGateway.PutAsync<PagedDataOperators, GetPropertyValuesAuditListResponseDto>(
            $"PropertyValuesAudit?page={safePage}&limit={safeLimit}&useAndLogic=true",
            operators,
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            return outcome;
        }

        return McpToolOutcome.Success(SecretRedactor.RedactPropertyValuesAudit(outcome.Data), outcome.HttpStatusCode ?? 200);
    }
}

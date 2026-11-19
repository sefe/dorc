using System.ComponentModel;
using Dorc.ApiModel;
using ModelContextProtocol.Server;

namespace Dorc.McpServer.Tools;

[McpServerToolType]
public sealed class PropertyResolutionTools
{
    private readonly IDorcApiGateway _apiGateway;
    private readonly ProductionEnvironmentGuard _guard;

    public PropertyResolutionTools(IDorcApiGateway apiGateway, ProductionEnvironmentGuard guard)
    {
        _apiGateway = apiGateway ?? throw new ArgumentNullException(nameof(apiGateway));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [McpServerTool(Name = "dorc_list_properties", ReadOnly = true)]
    [Description("Lists all property definitions registered in DOrc.")]
    public Task<McpToolOutcome<List<PropertyApiModel>>> ListPropertiesAsync(
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<PropertyApiModel>>("Properties", cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_property", ReadOnly = true)]
    [Description("Gets a property definition by numeric ID or property name.")]
    public Task<McpToolOutcome<PropertyApiModel>> GetPropertyAsync(
        [Description("Property ID or property name.")] string idOrName,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<PropertyApiModel>(
            $"Properties/id={Uri.EscapeDataString(idOrName.Trim())}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_property_values", ReadOnly = true)]
    [Description("Gets property values filtered by property name and/or environment name (at least one filter is required). Secure property values are automatically redacted.")]
    public async Task<McpToolOutcome<List<PropertyValueDto>>> GetPropertyValuesAsync(
        [Description("Optional property name filter (at least one of propertyName or environmentName must be provided).")] string? propertyName = null,
        [Description("Optional environment name filter (at least one of propertyName or environmentName must be provided).")] string? environmentName = null,
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

        if (queryParts.Count == 0)
        {
            return McpToolOutcome.Failure<List<PropertyValueDto>>(
                "At least one of 'propertyName' or 'environmentName' must be provided. Use 'dorc_search_property_values' for unfiltered paged searches.");
        }

        var path = $"PropertyValues?{string.Join("&", queryParts)}";

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

    [McpServerTool(Name = "dorc_create_property_value", Destructive = true)]
    [Description("Creates a non-secret property value for a specific environment scope. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<List<Response>>> CreatePropertyValueAsync(
        [Description("Property name.")] string propertyName,
        [Description("Non-secret property value.")] string value,
        [Description("Target environment scope name (e.g. 'Endur Dev 1').")] string environmentScope,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidatePropertyValueMutationAsync(
            "dorc_create_property_value",
            propertyName,
            environmentScope,
            cancellationToken).ConfigureAwait(false);

        if (validationError is not null)
        {
            return McpToolOutcome.Failure<List<Response>>(validationError);
        }

        var payload = BuildPropertyValuePayload(null, propertyName, value, environmentScope);
        return await _apiGateway.PostAsync<List<PropertyValueDto>, List<Response>>(
            "PropertyValues",
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_update_property_value", Destructive = true)]
    [Description("Updates an existing non-secret property value for a specific environment scope. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<List<Response>>> UpdatePropertyValueAsync(
        [Description("Numeric property value ID to update.")] long propertyValueId,
        [Description("Property name.")] string propertyName,
        [Description("Updated non-secret property value.")] string value,
        [Description("Target environment scope name (e.g. 'Endur Dev 1').")] string environmentScope,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidatePropertyValueMutationAsync(
            "dorc_update_property_value",
            propertyName,
            environmentScope,
            cancellationToken).ConfigureAwait(false);

        if (validationError is not null)
        {
            return McpToolOutcome.Failure<List<Response>>(validationError);
        }

        var payload = BuildPropertyValuePayload(propertyValueId, propertyName, value, environmentScope);
        return await _apiGateway.PutAsync<List<PropertyValueDto>, List<Response>>(
            "PropertyValues",
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_delete_property_value", Destructive = true)]
    [Description("Deletes a non-secret property value for a specific environment scope. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<List<Response>>> DeletePropertyValueAsync(
        [Description("Numeric property value ID to delete.")] long propertyValueId,
        [Description("Property name.")] string propertyName,
        [Description("Target environment scope name (e.g. 'Endur Dev 1').")] string environmentScope,
        CancellationToken cancellationToken = default)
    {
        var validationError = await ValidatePropertyValueMutationAsync(
            "dorc_delete_property_value",
            propertyName,
            environmentScope,
            cancellationToken).ConfigureAwait(false);

        if (validationError is not null)
        {
            return McpToolOutcome.Failure<List<Response>>(validationError);
        }

        var payload = BuildPropertyValuePayload(propertyValueId, propertyName, string.Empty, environmentScope);
        return await _apiGateway.DeleteAsync<List<PropertyValueDto>, List<Response>>(
            "PropertyValues",
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ValidatePropertyValueMutationAsync(
        string operationName,
        string propertyName,
        string environmentScope,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return $"Operation '{operationName}' requires a non-empty propertyName.";
        }

        var trimmedProperty = propertyName.Trim();
        if (SecretRedactor.IsLikelySensitiveName(trimmedProperty))
        {
            return $"Operation '{operationName}' on sensitive property '{trimmedProperty}' is blocked via MCP to prevent plaintext secret exposure.";
        }

        var guardError = await _guard.CheckEnvironmentMutationAllowedAsync(
            operationName,
            environmentScope,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return guardError;
        }

        var propOutcome = await _apiGateway.GetAsync<PropertyApiModel>(
            $"Properties/id={Uri.EscapeDataString(trimmedProperty)}",
            cancellationToken).ConfigureAwait(false);

        if (!propOutcome.Succeeded || propOutcome.Data is null)
        {
            return $"Operation '{operationName}' could not verify metadata for property '{trimmedProperty}' (HTTP {propOutcome.HttpStatusCode?.ToString() ?? "error"}: {propOutcome.Error}).";
        }

        if (propOutcome.Data.Secure)
        {
            return $"Operation '{operationName}' on secure property '{trimmedProperty}' (Secure=True) is blocked via MCP to prevent plaintext secret exposure.";
        }

        return null;
    }

    private static List<PropertyValueDto> BuildPropertyValuePayload(
        long? propertyValueId,
        string propertyName,
        string value,
        string environmentScope)
    {
        return
        [
            new PropertyValueDto
            {
                Id = propertyValueId ?? 0,
                Value = value,
                PropertyValueFilter = environmentScope.Trim(),
                Property = new PropertyApiModel
                {
                    Name = propertyName.Trim(),
                    Secure = false
                }
            }
        ];
    }
}

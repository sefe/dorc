using Dorc.ApiModel;

namespace Dorc.McpServer;

public sealed class ProductionEnvironmentGuard
{
    private readonly DorcMcpOptions _options;

    public ProductionEnvironmentGuard(DorcMcpOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsReadOnly => _options.ReadOnly;

    public bool AllowProd => _options.AllowProd;

    public string? CheckMutationAllowed(string operationName)
    {
        if (_options.ReadOnly)
        {
            return $"Operation '{operationName}' is blocked because Dorc.McpServer is running in read-only mode (DORC_MCP_READ_ONLY=true).";
        }

        return null;
    }

    public static bool IsProductionEnvironmentName(string? environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return false;
        }

        var trimmed = environmentName.Trim();
        return trimmed.Contains("PROD", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Contains("PRD", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string?> CheckEnvironmentMutationAllowedAsync(
        string operationName,
        string? environmentName,
        IDorcApiGateway apiGateway,
        CancellationToken cancellationToken)
    {
        var readOnlyError = CheckMutationAllowed(operationName);
        if (readOnlyError is not null)
        {
            return readOnlyError;
        }

        if (_options.AllowProd)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(environmentName))
        {
            return $"Operation '{operationName}' requires a non-empty target environment name when production protection is active.";
        }

        var trimmedName = environmentName.Trim();
        if (IsProductionEnvironmentName(trimmedName))
        {
            return $"Operation '{operationName}' targeting environment '{trimmedName}' is blocked because production mutations are disabled (DORC_MCP_ALLOW_PROD=false).";
        }

        var envOutcome = await apiGateway.GetAsync<List<EnvironmentApiModel>>(
            $"RefDataEnvironments?env={Uri.EscapeDataString(trimmedName)}",
            cancellationToken).ConfigureAwait(false);

        if (!envOutcome.Succeeded || envOutcome.Data is null)
        {
            return $"Operation '{operationName}' targeting environment '{trimmedName}' could not verify production status (HTTP {envOutcome.HttpStatusCode?.ToString() ?? "error"}: {envOutcome.Error}). Refusing to mutate when DORC_MCP_ALLOW_PROD=false.";
        }

        var matchingEnv = envOutcome.Data.FirstOrDefault(e =>
            string.Equals(e.EnvironmentName, trimmedName, StringComparison.OrdinalIgnoreCase));

        if (matchingEnv is null)
        {
            return $"Operation '{operationName}' could not find environment '{trimmedName}' to verify production status. Refusing to mutate when DORC_MCP_ALLOW_PROD=false.";
        }

        if (matchingEnv.EnvironmentIsProd)
        {
            return $"Operation '{operationName}' targeting production environment '{trimmedName}' (EnvironmentIsProd=true) is blocked because DORC_MCP_ALLOW_PROD=false.";
        }

        return null;
    }

    public async Task<string?> CheckRequestMutationAllowedAsync(
        string operationName,
        int requestId,
        IDorcApiGateway apiGateway,
        CancellationToken cancellationToken)
    {
        var readOnlyError = CheckMutationAllowed(operationName);
        if (readOnlyError is not null)
        {
            return readOnlyError;
        }

        if (_options.AllowProd)
        {
            return null;
        }

        var reqOutcome = await apiGateway.GetAsync<DeploymentRequestApiModel>(
            $"RequestStatuses?requestId={requestId}",
            cancellationToken).ConfigureAwait(false);

        if (!reqOutcome.Succeeded || reqOutcome.Data is null)
        {
            return $"Operation '{operationName}' for request {requestId} could not retrieve the deployment request to verify its target environment (HTTP {reqOutcome.HttpStatusCode?.ToString() ?? "error"}: {reqOutcome.Error}).";
        }

        return await CheckEnvironmentMutationAllowedAsync(
            operationName,
            reqOutcome.Data.EnvironmentName,
            apiGateway,
            cancellationToken).ConfigureAwait(false);
    }
}

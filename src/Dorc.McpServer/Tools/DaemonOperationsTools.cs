using System.ComponentModel;
using Dorc.ApiModel;
using ModelContextProtocol.Server;

namespace Dorc.McpServer.Tools;

[McpServerToolType]
public sealed class DaemonOperationsTools
{
    private static readonly HashSet<string> ValidRequestedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Starting",
        "Stopping",
        "Restarting"
    };

    private readonly IDorcApiGateway _apiGateway;
    private readonly ProductionEnvironmentGuard _guard;

    public DaemonOperationsTools(IDorcApiGateway apiGateway, ProductionEnvironmentGuard guard)
    {
        _apiGateway = apiGateway ?? throw new ArgumentNullException(nameof(apiGateway));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [McpServerTool(Name = "dorc_get_server_daemons", ReadOnly = true)]
    [Description("Lists daemons associated with a specific server ID.")]
    public Task<McpToolOutcome<List<DaemonApiModel>>> GetServerDaemonsAsync(
        [Description("Numeric server ID.")] int serverId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<DaemonApiModel>>(
            $"ServerDaemons/{serverId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_daemon_statuses_by_env_id", ReadOnly = true)]
    [Description("Gets live Windows service / daemon statuses for all servers in an environment by numeric environment ID.")]
    public Task<McpToolOutcome<List<DaemonStatusApiModel>>> GetDaemonStatusesByEnvironmentIdAsync(
        [Description("Numeric environment ID.")] int environmentId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<DaemonStatusApiModel>>(
            $"DaemonStatus?id={environmentId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_daemon_statuses_by_env_name", ReadOnly = true)]
    [Description("Gets live Windows service / daemon statuses for all servers in an environment by environment name.")]
    public Task<McpToolOutcome<List<DaemonStatusApiModel>>> GetDaemonStatusesByEnvironmentNameAsync(
        [Description("Environment name.")] string environmentName,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<DaemonStatusApiModel>>(
            $"DaemonStatus/{Uri.EscapeDataString(environmentName)}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_change_daemon_status", Destructive = true)]
    [Description("Starts, stops, or restarts a daemon on a server in an environment. requestedStatus must be 'Starting', 'Stopping', or 'Restarting'. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<string>> ChangeDaemonStatusAsync(
        [Description("Environment name.")] string environmentName,
        [Description("Server hostname.")] string serverName,
        [Description("Daemon / Windows service name.")] string daemonName,
        [Description("Requested state transition: Starting, Stopping, or Restarting.")] string requestedStatus,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestedStatus) || !ValidRequestedStatuses.Contains(requestedStatus.Trim()))
        {
            return McpToolOutcome.Failure<string>(
                "Invalid requestedStatus. Allowed values are 'Starting', 'Stopping', or 'Restarting'.");
        }

        var guardError = await _guard.CheckEnvironmentMutationAllowedAsync(
            "dorc_change_daemon_status",
            environmentName,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<string>(guardError);
        }

        var trimmedEnv = environmentName.Trim();
        var trimmedServer = (serverName ?? string.Empty).Trim();
        var trimmedDaemon = (daemonName ?? string.Empty).Trim();

        var statusesOutcome = await _apiGateway.GetAsync<List<DaemonStatusApiModel>>(
            $"DaemonStatus/{Uri.EscapeDataString(trimmedEnv)}",
            cancellationToken).ConfigureAwait(false);

        if (!statusesOutcome.Succeeded || statusesOutcome.Data is null)
        {
            return McpToolOutcome.Failure<string>(
                $"Operation 'dorc_change_daemon_status' could not retrieve mapped daemons for environment '{trimmedEnv}' (HTTP {statusesOutcome.HttpStatusCode?.ToString() ?? "error"}: {statusesOutcome.Error}).",
                statusesOutcome.HttpStatusCode);
        }

        var isMapped = statusesOutcome.Data.Any(d =>
            string.Equals(d.ServerName, trimmedServer, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.DaemonName, trimmedDaemon, StringComparison.OrdinalIgnoreCase));

        if (!isMapped)
        {
            return McpToolOutcome.Failure<string>(
                $"Daemon '{trimmedDaemon}' on server '{trimmedServer}' is not mapped to environment '{trimmedEnv}'.");
        }

        var normalizedStatus = NormalizeStatusCase(requestedStatus.Trim());
        var model = new DaemonStatusApiModel
        {
            EnvName = trimmedEnv,
            ServerName = trimmedServer,
            DaemonName = trimmedDaemon,
            Status = normalizedStatus
        };

        return await _apiGateway.PutStringResponseAsync(
            "DaemonStatus",
            model,
            cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeStatusCase(string status)
    {
        if (status.Equals("starting", StringComparison.OrdinalIgnoreCase))
        {
            return "Starting";
        }

        if (status.Equals("stopping", StringComparison.OrdinalIgnoreCase))
        {
            return "Stopping";
        }

        return "Restarting";
    }
}

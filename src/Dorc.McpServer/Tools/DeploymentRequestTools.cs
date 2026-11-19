using System.ComponentModel;
using Dorc.ApiModel;
using ModelContextProtocol.Server;

namespace Dorc.McpServer.Tools;

[McpServerToolType]
public sealed class DeploymentRequestTools
{
    private readonly IDorcApiGateway _apiGateway;
    private readonly ProductionEnvironmentGuard _guard;

    public DeploymentRequestTools(IDorcApiGateway apiGateway, ProductionEnvironmentGuard guard)
    {
        _apiGateway = apiGateway ?? throw new ArgumentNullException(nameof(apiGateway));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
    }

    [McpServerTool(Name = "dorc_search_deployment_requests", ReadOnly = true)]
    [Description("Searches deployment requests with optional filtering by project, environment, status, or user and returns a paginated list.")]
    public async Task<McpToolOutcome<GetRequestStatusesListResponseDto>> SearchDeploymentRequestsAsync(
        [Description("1-based page number (default 1).")] int page = 1,
        [Description("Maximum items per page (default 25, max 100).")] int limit = 25,
        [Description("Optional filter by Project name (contains match).")] string? project = null,
        [Description("Optional filter by EnvironmentName (contains match).")] string? environmentName = null,
        [Description("Optional filter by Status (e.g. Complete, Failed, Running, Pending, Cancelled).")] string? status = null,
        [Description("Optional filter by UserName (contains match).")] string? userName = null,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(1, page);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var filters = new List<PagedDataFilter>();

        AddContainsFilterIfPresent(filters, "Project", project);
        AddContainsFilterIfPresent(filters, "EnvironmentName", environmentName);
        AddContainsFilterIfPresent(filters, "Status", status);
        AddContainsFilterIfPresent(filters, "UserName", userName);

        var operators = new PagedDataOperators
        {
            Filters = filters,
            SortOrders =
            [
                new PagedDataSorting
                {
                    Path = "Id",
                    Direction = "desc"
                }
            ]
        };

        var outcome = await _apiGateway.PutAsync<PagedDataOperators, GetRequestStatusesListResponseDto>(
            $"RequestStatuses?page={safePage}&limit={safeLimit}",
            operators,
            cancellationToken).ConfigureAwait(false);

        if (outcome.Succeeded && outcome.Data?.Items is not null)
        {
            foreach (var item in outcome.Data.Items)
            {
                if (!string.IsNullOrEmpty(item.RequestDetails))
                {
                    item.RequestDetails = SecretRedactor.RedactRequestDetailsXml(item.RequestDetails);
                }

                if (!string.IsNullOrEmpty(item.Log))
                {
                    item.Log = LogTruncator.TruncateTail(item.Log);
                }
            }
        }

        return outcome;
    }

    [McpServerTool(Name = "dorc_get_deployment_request", ReadOnly = true)]
    [Description("Gets full deployment request details by request ID.")]
    public async Task<McpToolOutcome<DeploymentRequestApiModel>> GetDeploymentRequestAsync(
        [Description("Deployment request ID.")] int requestId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _apiGateway.GetAsync<DeploymentRequestApiModel>(
            $"RequestStatuses?requestId={requestId}",
            cancellationToken).ConfigureAwait(false);

        if (outcome.Succeeded && outcome.Data is not null)
        {
            if (!string.IsNullOrEmpty(outcome.Data.RequestDetails))
            {
                outcome.Data.RequestDetails = SecretRedactor.RedactRequestDetailsXml(outcome.Data.RequestDetails);
            }

            if (!string.IsNullOrEmpty(outcome.Data.Log))
            {
                outcome.Data.Log = LogTruncator.TruncateTail(outcome.Data.Log);
            }
        }

        return outcome;
    }

    [McpServerTool(Name = "dorc_get_deployment_request_status", ReadOnly = true)]
    [Description("Gets lightweight status for a deployment request by ID.")]
    public Task<McpToolOutcome<RequestStatusDto>> GetDeploymentRequestStatusAsync(
        [Description("Deployment request ID.")] int requestId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<RequestStatusDto>(
            $"Request?id={requestId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_deployment_component_results", ReadOnly = true)]
    [Description("Lists per-component deployment execution results for a deployment request.")]
    public Task<McpToolOutcome<List<DeploymentResultApiModel>>> GetDeploymentComponentResultsAsync(
        [Description("Deployment request ID.")] int requestId,
        CancellationToken cancellationToken = default)
    {
        return _apiGateway.GetAsync<List<DeploymentResultApiModel>>(
            $"ResultStatuses?requestId={requestId}",
            cancellationToken);
    }

    [McpServerTool(Name = "dorc_get_deployment_attempts", ReadOnly = true)]
    [Description("Lists previous deployment attempts for a deployment request.")]
    public async Task<McpToolOutcome<List<DeploymentRequestAttemptApiModel>>> GetDeploymentAttemptsAsync(
        [Description("Deployment request ID.")] int requestId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _apiGateway.GetAsync<List<DeploymentRequestAttemptApiModel>>(
            $"Request/{requestId}/attempts",
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Succeeded || outcome.Data is null)
        {
            return outcome;
        }

        foreach (var attempt in outcome.Data.Where(a => !string.IsNullOrEmpty(a.Log)))
        {
            attempt.Log = LogTruncator.TruncateTail(attempt.Log, LogTruncator.DefaultMaxCharacters);
        }

        return outcome;
    }

    [McpServerTool(Name = "dorc_get_deployment_component_log", ReadOnly = true)]
    [Description("Gets the execution log for a specific deployment component result, tail-truncated to preserve recent failure output.")]
    public async Task<McpToolOutcome<string>> GetDeploymentComponentLogAsync(
        [Description("Deployment request ID.")] int requestId,
        [Description("Component deployment result ID.")] int resultId,
        [Description("Maximum characters to return from the tail of the log (default 32768).")] int maxCharacters = LogTruncator.DefaultMaxCharacters,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _apiGateway.GetStringAsync(
            $"ResultStatuses/Log?requestId={requestId}&resultId={resultId}",
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            return outcome;
        }

        return McpToolOutcome.Success(LogTruncator.TruncateTail(outcome.Data, maxCharacters), outcome.HttpStatusCode ?? 200);
    }

    [McpServerTool(Name = "dorc_get_deployment_attempt_log", ReadOnly = true)]
    [Description("Gets the log for a historical deployment attempt component result, tail-truncated to preserve recent failure output.")]
    public async Task<McpToolOutcome<string>> GetDeploymentAttemptLogAsync(
        [Description("Deployment request ID.")] int requestId,
        [Description("Historical attempt deployment result ID.")] int deploymentResultId,
        [Description("Maximum characters to return from the tail of the log (default 32768).")] int maxCharacters = LogTruncator.DefaultMaxCharacters,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _apiGateway.GetStringAsync(
            $"Request/{requestId}/attempts/log?deploymentResultId={deploymentResultId}",
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            return outcome;
        }

        return McpToolOutcome.Success(LogTruncator.TruncateTail(outcome.Data, maxCharacters), outcome.HttpStatusCode ?? 200);
    }

    [McpServerTool(Name = "dorc_get_deployment_request_log", ReadOnly = true)]
    [Description("Gets the top-level deployment request orchestration log after verifying request visibility.")]
    public async Task<McpToolOutcome<string>> GetDeploymentRequestLogAsync(
        [Description("Deployment request ID.")] int requestId,
        [Description("Maximum characters to return from the tail of the log (default 32768).")] int maxCharacters = LogTruncator.DefaultMaxCharacters,
        CancellationToken cancellationToken = default)
    {
        var requestCheck = await _apiGateway.GetAsync<DeploymentRequestApiModel>(
            $"RequestStatuses?requestId={requestId}",
            cancellationToken).ConfigureAwait(false);

        if (!requestCheck.Succeeded || requestCheck.Data is null)
        {
            return McpToolOutcome.Failure<string>(
                requestCheck.Error ?? $"Deployment request {requestId} was not found or is not accessible.",
                requestCheck.HttpStatusCode);
        }

        return McpToolOutcome.Success(
            LogTruncator.TruncateTail(requestCheck.Data.Log, maxCharacters),
            requestCheck.HttpStatusCode ?? 200);
    }

    [McpServerTool(Name = "dorc_create_deployment_request", Destructive = true)]
    [Description("Creates a new deployment request in DOrc. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<RequestStatusDto>> CreateDeploymentRequestAsync(
        [Description("Project name.")] string project,
        [Description("Target environment name.")] string environment,
        [Description("Component names to deploy.")] List<string> components,
        [Description("Optional build artifact URL. Artifact-backed deployments require buildNum together with either buildUrl or buildText; omit build metadata only for artifact-free catalog deployments.")] string? buildUrl = null,
        [Description("Optional build number. Required for artifact-backed deployments alongside either buildUrl or buildText; omit only for artifact-free catalog deployments.")] string? buildNum = null,
        [Description("Optional build text / label. May be paired with buildNum instead of buildUrl for artifact-backed deployments.")] string? buildText = null,
        [Description("Whether to pin the request (default false).")] bool pinned = false,
        [Description("Optional list of deployment request properties.")] List<RequestProperty>? requestProperties = null,
        CancellationToken cancellationToken = default)
    {
        var guardError = await _guard.CheckEnvironmentMutationAllowedAsync(
            "dorc_create_deployment_request",
            environment,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<RequestStatusDto>(guardError);
        }

        var dto = new RequestDto
        {
            Project = project,
            Environment = environment,
            BuildUrl = buildUrl ?? string.Empty,
            BuildNum = buildNum ?? string.Empty,
            BuildText = buildText ?? string.Empty,
            Pinned = pinned,
            Components = components ?? [],
            RequestProperties = requestProperties ?? []
        };

        return await _apiGateway.PostAsync<RequestDto, RequestStatusDto>(
            "Request",
            dto,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_restart_deployment_request", Destructive = true)]
    [Description("Restarts an existing deployment request by ID. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<RequestStatusDto>> RestartDeploymentRequestAsync(
        [Description("Deployment request ID.")] int requestId,
        CancellationToken cancellationToken = default)
    {
        var guardError = await _guard.CheckRequestMutationAllowedAsync(
            "dorc_restart_deployment_request",
            requestId,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<RequestStatusDto>(guardError);
        }

        return await _apiGateway.PostAsync<object?, RequestStatusDto>(
            $"Request/restart?requestId={requestId}",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_cancel_deployment_request", Destructive = true)]
    [Description("Cancels an active or pending deployment request by ID. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<RequestStatusDto>> CancelDeploymentRequestAsync(
        [Description("Deployment request ID.")] int requestId,
        CancellationToken cancellationToken = default)
    {
        var guardError = await _guard.CheckRequestMutationAllowedAsync(
            "dorc_cancel_deployment_request",
            requestId,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<RequestStatusDto>(guardError);
        }

        return await _apiGateway.PutAsync<object?, RequestStatusDto>(
            $"Request/cancel?requestId={requestId}",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_pause_deployment_request", Destructive = true)]
    [Description("Pauses a pending deployment request by ID. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<RequestStatusDto>> PauseDeploymentRequestAsync(
        [Description("Deployment request ID.")] int requestId,
        CancellationToken cancellationToken = default)
    {
        var guardError = await _guard.CheckRequestMutationAllowedAsync(
            "dorc_pause_deployment_request",
            requestId,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<RequestStatusDto>(guardError);
        }

        return await _apiGateway.PutAsync<object?, RequestStatusDto>(
            $"Request/pause?requestId={requestId}",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dorc_resume_deployment_request", Destructive = true)]
    [Description("Resumes a paused deployment request by ID. Subject to read-only and production guardrails.")]
    public async Task<McpToolOutcome<RequestStatusDto>> ResumeDeploymentRequestAsync(
        [Description("Deployment request ID.")] int requestId,
        CancellationToken cancellationToken = default)
    {
        var guardError = await _guard.CheckRequestMutationAllowedAsync(
            "dorc_resume_deployment_request",
            requestId,
            _apiGateway,
            cancellationToken).ConfigureAwait(false);

        if (guardError is not null)
        {
            return McpToolOutcome.Failure<RequestStatusDto>(guardError);
        }

        return await _apiGateway.PutAsync<object?, RequestStatusDto>(
            $"Request/resume?requestId={requestId}",
            null,
            cancellationToken).ConfigureAwait(false);
    }

    private static void AddContainsFilterIfPresent(List<PagedDataFilter> filters, string path, string? filterValue)
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

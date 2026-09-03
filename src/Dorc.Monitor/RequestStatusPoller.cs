using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Monitor;

/// <summary>
/// Periodically polls the database to detect if a running request has been
/// cancelled or abandoned by another monitor instance, and triggers the
/// associated cancellation token to abort in-flight component execution.
/// 
/// Addresses HA scenario where Monitor A is processing a request and Monitor B
/// (or user via API) transitions it to Cancelled/Abandoned. Without this poller,
/// Monitor A would only observe the cancellation at the next component boundary,
/// potentially wasting hours on a component that should have been aborted.
/// </summary>
internal sealed class RequestStatusPoller : IDisposable
{
    private readonly IRequestsPersistentSource _requestsPersistentSource;
    private readonly ILogger<RequestStatusPoller> _logger;
    private readonly PeriodicTimer _timer;
    private readonly TimeSpan _pollInterval;
    private CancellationTokenSource? _disposalCts;
    private Task? _pollingTask;

    /// <summary>
    /// Terminal states that should trigger cancellation of an in-flight request.
    /// If a request transitions to any of these states while we're processing it,
    /// we must abort immediately.
    /// </summary>
    private static readonly HashSet<string> TerminalStopStates = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(DeploymentRequestStatus.Cancelled),
        nameof(DeploymentRequestStatus.Abandoned),
        nameof(DeploymentRequestStatus.Restarting),
    };

    public RequestStatusPoller(
        IRequestsPersistentSource requestsPersistentSource,
        ILogger<RequestStatusPoller> logger,
        TimeSpan? pollInterval = null)
    {
        _requestsPersistentSource = requestsPersistentSource;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(10);
        _timer = new PeriodicTimer(_pollInterval);
    }

    /// <summary>
    /// Starts monitoring the specified request. Returns immediately; polling
    /// runs on a background task until the request completes or is disposed.
    /// </summary>
    /// <param name="requestId">The deployment request ID to monitor.</param>
    /// <param name="cancellationTokenSource">The CancellationTokenSource to trigger if the request is cancelled externally.</param>
    /// <param name="stoppingToken">Monitor service shutdown token.</param>
    public void StartMonitoring(
        int requestId,
        CancellationTokenSource cancellationTokenSource,
        CancellationToken stoppingToken)
    {
        _disposalCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        
        _pollingTask = Task.Run(async () =>
        {
            _logger.LogInformation("Started status polling for request {RequestId} (interval: {Interval})", 
                requestId, _pollInterval);

            var wasRunning = false;

            try
            {
                while (await _timer.WaitForNextTickAsync(_disposalCts.Token))
                {
                    try
                    {
                        var currentRequest = _requestsPersistentSource.GetRequest(requestId);
                        
                        if (currentRequest == null)
                        {
                            _logger.LogWarning(
                                "Request {RequestId} not found during status polling - may have been deleted", 
                                requestId);
                            break;
                        }

                        if (string.Equals(currentRequest.Status, DeploymentRequestStatus.Running.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            wasRunning = true;
                        }

                        if (TerminalStopStates.Contains(currentRequest.Status))
                        {
                            _logger.LogWarning(
                                "Request {RequestId} transitioned to {Status} - triggering cancellation of in-flight execution",
                                requestId, currentRequest.Status);
                            
                            cancellationTokenSource.Cancel();
                            break;
                        }

                        if (wasRunning && string.Equals(currentRequest.Status, DeploymentRequestStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning(
                                "Request {RequestId} changed from Running to Pending - treating as restart and cancelling execution",
                                requestId);

                            cancellationTokenSource.Cancel();
                            break;
                        }

                        // If request reached a completion state naturally, stop polling
                        if (currentRequest.Status == DeploymentRequestStatus.Completed.ToString() ||
                            currentRequest.Status == DeploymentRequestStatus.Failed.ToString() ||
                            currentRequest.Status == DeploymentRequestStatus.Errored.ToString())
                        {
                            _logger.LogInformation(
                                "Request {RequestId} reached terminal state {Status} - stopping status polling",
                                requestId, currentRequest.Status);
                            break;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Log but don't stop polling - transient DB errors shouldn't break monitoring
                        _logger.LogWarning(ex,
                            "Error polling status for request {RequestId} - will retry on next interval",
                            requestId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown or disposal
                _logger.LogInformation("Status polling cancelled for request {RequestId}", requestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in status polling loop for request {RequestId}", requestId);
            }
        }, stoppingToken);
    }

    public void Dispose()
    {
        _disposalCts?.Cancel();
        _disposalCts?.Dispose();
        _timer.Dispose();
    }
}

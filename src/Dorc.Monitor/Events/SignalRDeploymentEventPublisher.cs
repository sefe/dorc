using Dorc.Core;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR.Client;
using TypedSignalR.Client;

namespace Dorc.Monitor.Events
{
    public sealed class SignalRDeploymentEventPublisher : IDeploymentEventsPublisher, IAsyncDisposable
    {
        private readonly string? _hubUrl;
        private readonly ILogger _logger;
        private readonly DorcApiTokenProvider _tokenProvider;
        private HubConnection? _connection;
        private IDeploymentEventsHub? _hubProxy;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private bool _isConnectionBecomeLost;
        private Timer? _tokenRefreshTimer;
        private const int TokenRefreshBufferMinutes = 2;
        private DateTime? _lastDisconnectTime;
        private bool _wasConnectedBefore;

        public SignalRDeploymentEventPublisher(IMonitorConfiguration configuration, ILogger<SignalRDeploymentEventPublisher> logger)
        {
            var baseUri = new Uri(configuration.RefDataApiUrl, UriKind.Absolute);
            _hubUrl = configuration.DisableSignalR ? null : new Uri(baseUri, "hubs/deployments").ToString();
            _logger = logger;

            _tokenProvider = new DorcApiTokenProvider(OAuthClientConfiguration.FromMonitorConfiguration(configuration));
        }

        public Task PublishNewRequestAsync(DeploymentRequestEventData eventData) =>
            PublishAsync(nameof(PublishNewRequestAsync), hub => hub.BroadcastNewRequestAsync(eventData));

        public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData) =>
            PublishAsync(nameof(PublishRequestStatusChangedAsync), hub => hub.BroadcastRequestStatusChangedAsync(eventData));

        public Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData) =>
            PublishAsync(nameof(PublishResultStatusChangedAsync), hub => hub.BroadcastResultStatusChangedAsync(eventData));

        private async Task PublishAsync(string operationName, Func<IDeploymentEventsHub, Task> action)
        {
            if (!await EnsureConnectionAsync(CancellationToken.None))
            {
                _logger.LogWarning("Cannot publish {Operation} - SignalR connection is not available", operationName);
                return;
            }

            try
            {
                await action(_hubProxy!);
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "Failed to invoke {Operation} via SignalR hub at {HubUrl}", operationName, _hubUrl);
                throw;
            }
        }

        private async Task<bool> EnsureConnectionAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_hubUrl))
            {
                return false; // disabled
            }

            if (_connection is { State: HubConnectionState.Connected })
            {
                var timeUntilExpiration = _tokenProvider.TimeUntilExpiration;
                if (timeUntilExpiration < TimeSpan.FromMinutes(TokenRefreshBufferMinutes))
                {
                    _logger.LogInformation(
                        "Token expires in {TimeRemaining:F1} minutes, proactively reconnecting SignalR",
                        timeUntilExpiration.TotalMinutes);
                    await ReconnectAsync(ct);
                }
                return true;
            }

            await _connectionLock.WaitAsync(ct);
            try
            {
                if (_connection == null)
                {
                    var builder = new HubConnectionBuilder()
                        .WithUrl(_hubUrl, options =>
                        {
                            options.AccessTokenProvider = async () =>
                            {
                                try
                                {
                                    var token = await _tokenProvider.GetTokenAsync();

                                    ScheduleTokenRefresh();

                                    var expiresAt = _tokenProvider.TokenExpiresAtUtc;
                                    _logger.LogDebug("SignalR token acquired, expires at {ExpiresAt:u}", expiresAt);

                                    return token;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to acquire OAuth access token for SignalR connection");
                                    return string.Empty;
                                }
                            };
                        })
                        .WithAutomaticReconnect(new[] 
                        {
                            TimeSpan.Zero,
                            TimeSpan.FromSeconds(2),
                            TimeSpan.FromSeconds(5),
                            TimeSpan.FromSeconds(10),
                            TimeSpan.FromSeconds(30),
                            TimeSpan.FromMinutes(1),
                            TimeSpan.FromMinutes(2)
                        });

                    _connection = builder.Build();

                    // Register connection event handlers
                    RegisterConnectionHandlers();
                }

                if (_connection.State == HubConnectionState.Disconnected)
                {
                    _logger.LogInformation("Starting SignalR connection to {HubUrl}...", _hubUrl);
                    await _connection.StartAsync(ct);
                }

                if (_hubProxy == null)
                {
                    _hubProxy = _connection.CreateHubProxy<IDeploymentEventsHub>(ct);
                }

                // we have to register also client for hub in order to eliminate signalR errors when event is broadcasted to all clients
                _connection.Register<IDeploymentsEventsClient>(new NullDeploymentsEventsClient());

                if (_connection.State == HubConnectionState.Connected)
                {
                    if (_isConnectionBecomeLost)
                    {
                        var downtime = _lastDisconnectTime.HasValue 
                            ? DateTime.UtcNow - _lastDisconnectTime.Value 
                            : TimeSpan.Zero;
                        _logger.LogInformation(
                            "SignalR connection restored successfully after {Downtime:F1} seconds",
                            downtime.TotalSeconds);
                    }
                    _isConnectionBecomeLost = false;
                    _wasConnectedBefore = true;
                    _lastDisconnectTime = null;
                    return true;
                }

                return false;
            }
            catch (Exception exc)
            {
                if (!_isConnectionBecomeLost)
                {
                    _logger.LogError(exc, "Network error: Failed to connect to SignalR hub at {HubUrl}. Will retry automatically...", _hubUrl);
                    _isConnectionBecomeLost = true;
                    _lastDisconnectTime = DateTime.UtcNow;
                }
                return false;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private void RegisterConnectionHandlers()
        {
            if (_connection == null) return;

            _connection.Closed += async (error) =>
            {
                if (error != null)
                {
                    _logger.LogWarning("SignalR connection closed due to error: {ErrorMessage}. Automatic reconnection will be attempted...", 
                        error.Message);
                }
                else
                {
                    _logger.LogInformation("SignalR connection closed");
                }
                
                if (!_isConnectionBecomeLost)
                {
                    _isConnectionBecomeLost = true;
                    _lastDisconnectTime = DateTime.UtcNow;
                }

                await Task.CompletedTask;
            };

            _connection.Reconnecting += (error) =>
            {
                if (error != null)
                {
                    _logger.LogWarning("Network disconnected. Attempting to reconnect to SignalR hub... (Error: {ErrorMessage})", 
                        error.Message);
                }
                else
                {
                    _logger.LogInformation("SignalR connection lost. Attempting to reconnect...");
                }

                if (!_isConnectionBecomeLost)
                {
                    _isConnectionBecomeLost = true;
                    _lastDisconnectTime = DateTime.UtcNow;
                }

                return Task.CompletedTask;
            };

            _connection.Reconnected += (connectionId) =>
            {
                var downtime = _lastDisconnectTime.HasValue 
                    ? DateTime.UtcNow - _lastDisconnectTime.Value 
                    : TimeSpan.Zero;

                _logger.LogInformation(
                    "Successfully reconnected to SignalR hub after {Downtime:F1} seconds. Connection ID: {ConnectionId}",
                    downtime.TotalSeconds,
                    connectionId ?? "N/A");

                _isConnectionBecomeLost = false;
                _lastDisconnectTime = null;

                return Task.CompletedTask;
            };
        }

        private void ScheduleTokenRefresh()
        {
            var timeUntilExpiration = _tokenProvider.TimeUntilExpiration;
            if (timeUntilExpiration <= TimeSpan.Zero)
                return;

            var refreshTime = timeUntilExpiration - TimeSpan.FromMinutes(TokenRefreshBufferMinutes);

            if (refreshTime.TotalSeconds > 0)
            {
                _tokenRefreshTimer?.Dispose();
                _tokenRefreshTimer = new Timer(
                    async _ => await RefreshConnectionAsync(),
                    null,
                    refreshTime,
                    Timeout.InfiniteTimeSpan);

                _logger.LogDebug(
                    "Scheduled SignalR token refresh in {RefreshMinutes:F1} minutes (token expires in {ExpiresMinutes:F1} minutes)",
                    refreshTime.TotalMinutes,
                    timeUntilExpiration.TotalMinutes);
            }
            else
            {
                _tokenRefreshTimer?.Dispose();
                _tokenRefreshTimer = new Timer(
                    async _ => await RefreshConnectionAsync(),
                    null,
                    TimeSpan.FromSeconds(10),
                    Timeout.InfiniteTimeSpan);

                _logger.LogWarning(
                    "Token expires in {ExpiresMinutes:F1} minutes, scheduling immediate refresh",
                    timeUntilExpiration.TotalMinutes);
            }
        }

        private async Task RefreshConnectionAsync()
        {
            try
            {
                _logger.LogInformation("Proactively refreshing SignalR connection before token expires");
                await ReconnectAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh SignalR connection");
            }
        }

        private async Task ReconnectAsync(CancellationToken ct)
        {
            await _connectionLock.WaitAsync(ct);
            try
            {
                if (_connection != null)
                {
                    _logger.LogInformation("Stopping existing SignalR connection for token refresh");
                    await _connection.StopAsync(ct);
                    await _connection.DisposeAsync();
                    _connection = null;
                    _hubProxy = null;
                }

                _logger.LogInformation("SignalR connection disposed, reconnecting with fresh token");
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _tokenRefreshTimer?.Dispose();
            if (_connection != null)
            {
                await _connection.DisposeAsync();
            }
            _connectionLock.Dispose();
            await _tokenProvider.DisposeAsync();
        }

        private sealed class NullDeploymentsEventsClient : IDeploymentsEventsClient
        {
            public Task OnDeploymentRequestStatusChanged(DeploymentRequestEventData data) => Task.CompletedTask;
            public Task OnDeploymentRequestStarted(DeploymentRequestEventData data) => Task.CompletedTask;
            public Task OnDeploymentResultStatusChanged(DeploymentResultEventData data) => Task.CompletedTask;
        }
    }
}



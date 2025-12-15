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
                _logger.LogWarning($"Cannot publish {operationName} - SignalR connection is not available");
                return;
            }

            try
            {
                await action(_hubProxy!);
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, $"Failed to invoke {operationName} via SignalR hub at {_hubUrl}");
                throw;
            }
        }

        private async Task<bool> EnsureConnectionAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_hubUrl))
            {
                return false; // disabled
            }

            // Check token expiration
            var timeUntilExpiration = _tokenProvider.TimeUntilExpiration;
            if (timeUntilExpiration < TimeSpan.FromMinutes(TokenRefreshBufferMinutes))
            {
                _logger.LogInformation("Token expiring, recreating connection");
                await RecreateConnectionAsync(ct);
            }

            if (_connection is { State: HubConnectionState.Connected })
            {
                return true;
            }

            await _connectionLock.WaitAsync(ct);
            try
            {
                if (_connection == null)
                {
                    CreateConnection();
                }

                if (_connection!.State == HubConnectionState.Disconnected)
                {
                    _logger.LogInformation("Starting SignalR connection to {HubUrl}...", _hubUrl);
                    await _connection.StartAsync(ct);
                    
                    // Schedule token refresh after successful connection
                    if (_connection.State == HubConnectionState.Connected)
                    {
                        ScheduleTokenRefresh();
                    }
                }

                if (_hubProxy == null)
                {
                    _hubProxy = _connection.CreateHubProxy<IDeploymentEventsHub>(ct);
                }

                _connection.Register<IDeploymentsEventsClient>(new NullDeploymentsEventsClient());

                if (_connection.State == HubConnectionState.Connected)
                {
                    if (_isConnectionBecomeLost)
                    {
                        var downtime = _lastDisconnectTime.HasValue 
                            ? DateTime.UtcNow - _lastDisconnectTime.Value 
                            : TimeSpan.Zero;
                        _logger.LogInformation("SignalR connection restored after {Downtime:F1} seconds", downtime.TotalSeconds);
                    }
                    _isConnectionBecomeLost = false;
                    _lastDisconnectTime = null;
                    return true;
                }

                return false;
            }
            catch (Exception exc)
            {
                if (!_isConnectionBecomeLost)
                {
                    _logger.LogError(exc, $"Error connecting to SignalR hub at {_hubUrl}");
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

        private void CreateConnection()
        {
            var builder = new HubConnectionBuilder()
                .WithUrl(_hubUrl!, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        try
                        {
                            var token = await _tokenProvider.GetTokenAsync();
                            _logger.LogDebug("SignalR token acquired, expires at {ExpiresAt:u}", _tokenProvider.TokenExpiresAtUtc);
                            return token;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to acquire OAuth token");
                            return string.Empty;
                        }
                    };
                })
                .WithAutomaticReconnect(new[] 
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10)
                });

            _connection = builder.Build();
            RegisterConnectionHandlers();
        }

        private void RegisterConnectionHandlers()
        {
            if (_connection == null) return;

            _connection.Closed += async (error) =>
            {
                if (error != null)
                {
                    _logger.LogWarning("SignalR connection closed: {ErrorMessage}", error.Message);
                }
                
                if (!_isConnectionBecomeLost)
                {
                    _isConnectionBecomeLost = true;
                    _lastDisconnectTime = DateTime.UtcNow;
                }
                
                // Cancel token refresh when connection is closed
                _tokenRefreshTimer?.Dispose();
                _tokenRefreshTimer = null;

                await Task.CompletedTask;
            };

            _connection.Reconnecting += (error) =>
            {
                _logger.LogWarning("SignalR reconnecting...");
                if (!_isConnectionBecomeLost)
                {
                    _isConnectionBecomeLost = true;
                    _lastDisconnectTime = DateTime.UtcNow;
                }
                
                // Cancel token refresh during reconnection
                _tokenRefreshTimer?.Dispose();
                _tokenRefreshTimer = null;
                
                return Task.CompletedTask;
            };

            _connection.Reconnected += (connectionId) =>
            {
                _logger.LogInformation("SignalR reconnected. Connection ID: {ConnectionId}", connectionId ?? "N/A");
                _isConnectionBecomeLost = false;
                _lastDisconnectTime = null;
                
                // Schedule token refresh after successful reconnection
                ScheduleTokenRefresh();
                
                return Task.CompletedTask;
            };
        }

        private void ScheduleTokenRefresh()
        {
            var timeUntilExpiration = _tokenProvider.TimeUntilExpiration;
            if (timeUntilExpiration <= TimeSpan.Zero)
            {
                _logger.LogWarning("Token already expired, cannot schedule refresh");
                return;
            }

            var refreshTime = timeUntilExpiration - TimeSpan.FromMinutes(TokenRefreshBufferMinutes);
            if (refreshTime.TotalSeconds < 10)
                refreshTime = TimeSpan.FromSeconds(10);

            _tokenRefreshTimer?.Dispose();
            _tokenRefreshTimer = new Timer(
                OnTokenRefreshTimerCallback,
                null,
                refreshTime,
                Timeout.InfiniteTimeSpan);

            _logger.LogDebug("Token refresh scheduled in {Minutes:F1} minutes", refreshTime.TotalMinutes);
        }

        private void OnTokenRefreshTimerCallback(object? state)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RecreateConnectionAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during scheduled token refresh and connection recreation");
                }
            });
        }

        private async Task RecreateConnectionAsync(CancellationToken ct)
        {
            await _connectionLock.WaitAsync(ct);
            try
            {
                // Cancel any pending token refresh
                _tokenRefreshTimer?.Dispose();
                _tokenRefreshTimer = null;
                
                if (_connection != null)
                {
                    _logger.LogInformation("Recreating SignalR connection with fresh token");
                    await _connection.StopAsync(ct);
                    await _connection.DisposeAsync();
                    _connection = null;
                    _hubProxy = null;
                }
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



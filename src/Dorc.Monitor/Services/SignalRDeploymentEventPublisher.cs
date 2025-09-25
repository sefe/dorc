using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using log4net;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using TypedSignalR.Client;
using Dorc.Monitor.Logging;

namespace Dorc.Monitor.Services
{
    public sealed class SignalRDeploymentEventPublisher : IDeploymentEventsPublisher, IAsyncDisposable
    {
        private readonly string _hubUrl;
        private readonly ILog _logger;
        private readonly string? _accessToken;
        private HubConnection? _connection;
        private IDeploymentEventsPublisher? _hubProxy;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private bool _isConnectionBecomeLost;

        public SignalRDeploymentEventPublisher(IMonitorConfiguration configuration, ILog logger)
        {
            var baseUri = new Uri(configuration.RefDataApiUrl, UriKind.Absolute);
            _hubUrl = new Uri(baseUri, "hubs/deployments").ToString();
            //_accessToken = configuration["Internal:SignalR:AccessToken"];
            _logger = logger;
        }

        public async Task PublishNewRequestAsync(DeploymentEventData eventData)
        {
            if (!await EnsureConnectionAsync(CancellationToken.None))
            {
                return;
            }
            try
            {
                await _hubProxy!.PublishNewRequestAsync(eventData);
            }
            catch (Exception exc)
            {
                _logger.Error($"Failed to invoke PublishNewRequestAsync via SignalR hub at {_hubUrl}", exc);
                throw;
            }
        }

        public async Task PublishRequestStatusChangedAsync(DeploymentEventData eventData)
        {
            if (!await EnsureConnectionAsync(CancellationToken.None))
            {
                return;
            }
            try
            {
                await _hubProxy!.PublishRequestStatusChangedAsync(eventData);
            }
            catch (Exception exc)
            {
                _logger.Error($"Failed to invoke PublishRequestStatusChangedAsync via SignalR hub at {_hubUrl}", exc);
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
                _isConnectionBecomeLost = false;
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
                            options.UseDefaultCredentials = true;

                            if (!string.IsNullOrWhiteSpace(_accessToken))
                            {
                                options.AccessTokenProvider = () => Task.FromResult(_accessToken)!;
                            }
                        })
                        .ConfigureLogging(logging =>
                        {
                            logging.ClearProviders();
                            logging.AddProvider(new Log4NetLoggerProvider(_logger));
                        })
                        .WithAutomaticReconnect();
                    _connection = builder.Build();
                }

                if (_connection.State == HubConnectionState.Disconnected)
                {
                    await _connection.StartAsync(ct);
                }

                if (_hubProxy == null)
                {
                    _hubProxy = _connection.CreateHubProxy<IDeploymentEventsPublisher>(ct);
                }

                if (_connection.State == HubConnectionState.Connected)
                {
                    _isConnectionBecomeLost = false;
                    return true;
                }

                return false;
            }
            catch (Exception exc)
            {
                if (!_isConnectionBecomeLost)
                {
                    _logger.Error($"Error connecting to SignalR hub at {_hubUrl}", exc);
                    _isConnectionBecomeLost = true;
                }
                return false;
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
            {
                await _connection.DisposeAsync();
            }
            _connectionLock.Dispose();
        }
    }
}



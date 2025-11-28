using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Dorc.Monitor.HighAvailability
{
    /// <summary>
    /// RabbitMQ-based distributed lock service using quorum queues with single-active consumer pattern.
    /// Supports RabbitMQ clusters with automatic failover and ensures only one monitor instance processes deployments per environment.
    /// Uses OAuth 2.0 client credentials flow for authentication.
    /// </summary>
    public class RabbitMqDistributedLockService : IDistributedLockService, IDisposable
    {
        private readonly ILogger<RabbitMqDistributedLockService> logger;
        private readonly IMonitorConfiguration configuration;
        // HttpClient instance is managed by IHttpClientFactory and must NOT be disposed manually
        private readonly HttpClient httpClient;
        private IConnection? connection;
        private readonly SemaphoreSlim connectionSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim tokenSemaphore = new SemaphoreSlim(1, 1);
        private bool disposed = false;
        private string? cachedToken;
        private DateTime tokenExpiry = DateTime.MinValue;

        public RabbitMqDistributedLockService(
            ILogger<RabbitMqDistributedLockService> logger,
            IMonitorConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            this.logger = logger;
            this.configuration = configuration;
            this.httpClient = httpClientFactory.CreateClient();
        }

        public bool IsEnabled => configuration.HighAvailabilityEnabled;

        public async Task<IDistributedLock?> TryAcquireLockAsync(string resourceKey, int leaseTimeMs, CancellationToken cancellationToken)
        {
            if (!IsEnabled)
            {
                logger.LogDebug("Distributed locking is disabled, returning null lock for resource '{ResourceKey}'", resourceKey);
                return null;
            }

            try
            {
                await EnsureConnectionAsync();
                
                if (connection == null || !connection.IsOpen)
                {
                    logger.LogWarning("RabbitMQ connection is not available for lock acquisition on '{ResourceKey}'", resourceKey);
                    return null;
                }

                var channel = await connection.CreateChannelAsync();
                
                // Use environment-specific exchange to support multiple DOrc instances (Prod/Staging/Dev) in same RabbitMQ cluster
                // Sanitize environment name: lowercase, replace spaces and special chars with hyphens
                var environment = SanitizeEnvironmentName(configuration.Environment);
                var exchangeName = $"dorc.{environment}";
                var queueName = $"lock.{resourceKey}";
                
                try
                {
                    // Declare a direct exchange for this DOrc environment
                    await channel.ExchangeDeclareAsync(
                        exchange: exchangeName,
                        type: ExchangeType.Direct,
                        durable: true,
                        autoDelete: false);

                    // Declare a quorum queue with single-active consumer for cluster support
                    // Quorum queues replicate across cluster nodes for high availability
                    var args = new Dictionary<string, object>
                    {
                        { "x-queue-type", "quorum" }, // Use quorum queue for cluster replication
                        { "x-single-active-consumer", true } // Only one consumer gets messages at a time
                    };

                    await channel.QueueDeclareAsync(
                        queue: queueName,
                        durable: true, // Quorum queues must be durable
                        exclusive: false, // Quorum queues don't support exclusive mode
                        autoDelete: false, // Keep queue for lock reacquisition
                        arguments: args);

                    // Bind queue to environment-specific exchange
                    await channel.QueueBindAsync(
                        queue: queueName,
                        exchange: exchangeName,
                        routingKey: queueName);

                    // Publish a lock message that the consumer will hold
                    var lockMessage = System.Text.Encoding.UTF8.GetBytes($"lock:{resourceKey}:{DateTime.UtcNow:O}");
                    await channel.BasicPublishAsync(
                        exchange: exchangeName,
                        routingKey: queueName,
                        body: lockMessage);

                    // Start consuming - single-active consumer ensures only one monitor processes this
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    
                    // Attach event handler to receive and hold the message
                    consumer.ReceivedAsync += async (model, ea) =>
                    {
                        // Message received - lock is held by NOT acknowledging
                        // The lock will be released when consumer is cancelled (in Dispose)
                        await Task.CompletedTask;
                    };
                    
                    var consumerTag = await channel.BasicConsumeAsync(
                        queue: queueName,
                        autoAck: false, // Manual ack - lock held until we ack or consumer disconnects
                        consumer: consumer);

                    logger.LogDebug("Successfully acquired distributed lock for resource '{ResourceKey}' using quorum queue with single-active consumer", 
                        resourceKey);

                    return new RabbitMqDistributedLock(logger, channel, queueName, resourceKey, consumerTag);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error acquiring lock for '{ResourceKey}'", resourceKey);
                    await channel.CloseAsync();
                    await channel.DisposeAsync();
                    return null;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to acquire distributed lock for '{ResourceKey}'", resourceKey);
                return null;
            }
        }

        private async Task EnsureConnectionAsync()
        {
            await connectionSemaphore.WaitAsync();
            try
            {
                if (connection != null && connection.IsOpen)
                    return;

                if (!IsEnabled)
                    return;

                try
                {
                    // Get OAuth token using client credentials flow
                    var token = await GetOAuthTokenAsync();
                    
                    var factory = new ConnectionFactory
                    {
                        HostName = configuration.RabbitMqHostName,
                        Port = configuration.RabbitMqPort,
                        VirtualHost = configuration.RabbitMqVirtualHost ?? "/",
                        RequestedHeartbeat = TimeSpan.FromSeconds(60),
                        AutomaticRecoveryEnabled = true,
                        NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                        // Use OAuth token as username with empty password
                        // RabbitMQ OAuth plugin expects token in username field
                        UserName = token,
                        Password = ""
                    };

                    connection = await factory.CreateConnectionAsync($"DOrc.Monitor-{Environment.MachineName}-{Guid.NewGuid()}");
                    logger.LogInformation("Established RabbitMQ connection to {HostName}:{Port} using OAuth", 
                        configuration.RabbitMqHostName, configuration.RabbitMqPort);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to connect to RabbitMQ at {HostName}:{Port}", 
                        configuration.RabbitMqHostName, configuration.RabbitMqPort);
                    throw;
                }
            }
            finally
            {
                connectionSemaphore.Release();
            }
        }

        private async Task<string> GetOAuthTokenAsync()
        {
            await tokenSemaphore.WaitAsync();
            try
            {
                // Check if we have a cached token that's still valid
                if (!string.IsNullOrEmpty(cachedToken) && DateTime.UtcNow < tokenExpiry)
                {
                    return cachedToken;
                }

                logger.LogDebug("Acquiring OAuth token from {TokenEndpoint}", configuration.RabbitMqOAuthTokenEndpoint);

                // Prepare OAuth 2.0 client credentials request
                var requestBody = new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "client_id", configuration.RabbitMqOAuthClientId },
                    { "client_secret", configuration.RabbitMqOAuthClientSecret }
                };

                // Add scope if provided
                if (!string.IsNullOrWhiteSpace(configuration.RabbitMqOAuthScope))
                {
                    requestBody.Add("scope", configuration.RabbitMqOAuthScope);
                }

                using (var request = new HttpRequestMessage(HttpMethod.Post, configuration.RabbitMqOAuthTokenEndpoint)
                {
                    Content = new FormUrlEncodedContent(requestBody)
                })
                {
                    var response = await httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    var tokenResponse = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>();
                    
                    if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
                    {
                        throw new InvalidOperationException("Failed to obtain OAuth token - empty response");
                    }

                    // Cache the token with a 10-second buffer before expiry
                    cachedToken = tokenResponse.AccessToken;
                    tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 10);

                    logger.LogDebug("Successfully acquired OAuth token, expires in {ExpiresIn} seconds", tokenResponse.ExpiresIn);

                    return cachedToken;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to acquire OAuth token from {TokenEndpoint}", configuration.RabbitMqOAuthTokenEndpoint);
                throw;
            }
            finally
            {
                tokenSemaphore.Release();
            }
        }

        /// <summary>
        /// Sanitizes the environment name for use in RabbitMQ exchange names.
        /// Converts to lowercase and replaces spaces and special characters with hyphens.
        /// </summary>
        private static string SanitizeEnvironmentName(string environment)
        {
            if (string.IsNullOrWhiteSpace(environment))
            {
                return "default";
            }

            // Convert to lowercase
            var sanitized = environment.ToLowerInvariant();
            
            // Replace spaces and invalid characters with hyphens
            // RabbitMQ exchange names can contain: letters, digits, hyphen, underscore, period, colon
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[^a-z0-9\-_.:]+", "-");
            
            // Remove leading/trailing hyphens
            sanitized = sanitized.Trim('-');
            
            // If empty after sanitization, use default
            return string.IsNullOrEmpty(sanitized) ? "default" : sanitized;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            
            IConnection? connToDispose = null;
            connectionSemaphore.Wait();
            try
            {
                connToDispose = connection;
                connection = null;
            }
            finally
            {
                connectionSemaphore.Release();
                connectionSemaphore.Dispose();
            }

            tokenSemaphore.Dispose();

            if (connToDispose != null)
            {
                try
                {
                    connToDispose.CloseAsync().GetAwaiter().GetResult();
                    connToDispose.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error closing RabbitMQ connection");
                }
            }

            // Note: httpClient is created from IHttpClientFactory and should NOT be disposed manually
            // The factory manages the lifecycle of HttpClient instances
        }
    }

    /// <summary>
    /// OAuth 2.0 token response.
    /// </summary>
    internal class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    /// <summary>
    /// Represents a lock held via a RabbitMQ quorum queue with single-active consumer.
    /// </summary>
    internal class RabbitMqDistributedLock : IDistributedLock
    {
        private readonly ILogger logger;
        private IChannel? channel;
        private readonly string queueName;
        private readonly string consumerTag;
        private bool disposed = false;
        private readonly object disposeLock = new object();

        public string ResourceKey { get; }

        public RabbitMqDistributedLock(ILogger logger, IChannel channel, string queueName, string resourceKey, string consumerTag)
        {
            this.logger = logger;
            this.channel = channel;
            this.queueName = queueName;
            this.ResourceKey = resourceKey;
            this.consumerTag = consumerTag;
        }

        public void Dispose()
        {
            lock (disposeLock)
            {
                if (disposed)
                    return;

                disposed = true;
                
                try
                {
                    if (channel != null && channel.IsOpen)
                    {
                        try
                        {
                            // Cancel the consumer to release the single-active consumer slot
                            channel.BasicCancelAsync(consumerTag).GetAwaiter().GetResult();
                            logger.LogDebug("Released distributed lock for resource '{ResourceKey}'", ResourceKey);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Error canceling consumer for '{ResourceKey}'", ResourceKey);
                        }

                        channel.CloseAsync().GetAwaiter().GetResult();
                        channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error disposing distributed lock for '{ResourceKey}'", ResourceKey);
                }
                finally
                {
                    channel = null;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (disposeLock)
            {
                if (disposed)
                    return;

                disposed = true;
            }
            
            if (channel != null && channel.IsOpen)
            {
                try
                {
                    // Cancel the consumer to release the single-active consumer slot
                    await channel.BasicCancelAsync(consumerTag);
                    logger.LogDebug("Released distributed lock for resource '{ResourceKey}'", ResourceKey);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error canceling consumer for '{ResourceKey}'", ResourceKey);
                }

                try
                {
                    await channel.CloseAsync();
                    await channel.DisposeAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error disposing channel for '{ResourceKey}'", ResourceKey);
                }
            }
            
            channel = null;
        }
    }
}

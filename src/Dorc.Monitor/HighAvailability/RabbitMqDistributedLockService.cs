using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Http;

namespace Dorc.Monitor.HighAvailability
{
    /// <summary>
    /// RabbitMQ-based distributed lock service using message queues with TTL for lock management.
    /// Each lock is represented by an exclusive queue - only one consumer can connect to it at a time.
    /// Uses OAuth 2.0 for authentication.
    /// </summary>
    public class RabbitMqDistributedLockService : IDistributedLockService, IDisposable
    {
        private readonly ILogger<RabbitMqDistributedLockService> logger;
        private readonly IMonitorConfiguration configuration;
        private readonly HttpClient httpClient;
        private IConnection? connection;
        private readonly object connectionLock = new object();
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
                EnsureConnection();
                
                if (connection == null || !connection.IsOpen)
                {
                    logger.LogWarning("RabbitMQ connection is not available for lock acquisition on '{ResourceKey}'", resourceKey);
                    return null;
                }

                var channel = connection.CreateModel();
                var queueName = $"dorc.lock.{resourceKey}";
                
                try
                {
                    // Try to declare an exclusive queue - only one consumer can hold it
                    // If another monitor already has this lock, this will throw OperationInterruptedException
                    var args = new Dictionary<string, object>
                    {
                        // Auto-delete the queue when the consumer disconnects (failover handling)
                        { "x-expires", leaseTimeMs + 5000 } // Queue expires slightly after lease time if not deleted
                    };

                    channel.QueueDeclare(
                        queue: queueName,
                        durable: false,
                        exclusive: true, // This is the key - only one connection can have this queue
                        autoDelete: true,
                        arguments: args);

                    logger.LogDebug("Successfully acquired distributed lock for resource '{ResourceKey}' with lease {LeaseMs}ms", 
                        resourceKey, leaseTimeMs);

                    return new RabbitMqDistributedLock(logger, channel, queueName, resourceKey);
                }
                catch (OperationInterruptedException ex) when (ex.ShutdownReason.ReplyCode == 405) // RESOURCE_LOCKED
                {
                    logger.LogDebug("Failed to acquire lock for '{ResourceKey}' - already locked by another instance", resourceKey);
                    channel.Close();
                    channel.Dispose();
                    return null;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error acquiring lock for '{ResourceKey}'", resourceKey);
                    channel.Close();
                    channel.Dispose();
                    throw;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to acquire distributed lock for '{ResourceKey}'", resourceKey);
                return null;
            }
        }

        private void EnsureConnection()
        {
            lock (connectionLock)
            {
                if (connection != null && connection.IsOpen)
                    return;

                if (!IsEnabled)
                    return;

                try
                {
                    // Get OAuth token
                    var token = GetOAuthTokenAsync().GetAwaiter().GetResult();
                    
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

                    connection = factory.CreateConnection($"DOrc.Monitor-{Environment.MachineName}-{Guid.NewGuid()}");
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
        }

        private async Task<string> GetOAuthTokenAsync()
        {
            // Check if we have a cached token that's still valid
            if (!string.IsNullOrEmpty(cachedToken) && DateTime.UtcNow < tokenExpiry)
            {
                return cachedToken;
            }

            try
            {
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

                var request = new HttpRequestMessage(HttpMethod.Post, configuration.RabbitMqOAuthTokenEndpoint)
                {
                    Content = new FormUrlEncodedContent(requestBody)
                };

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
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to acquire OAuth token from {TokenEndpoint}", configuration.RabbitMqOAuthTokenEndpoint);
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            lock (connectionLock)
            {
                if (connection != null)
                {
                    try
                    {
                        connection.Close();
                        connection.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error closing RabbitMQ connection");
                    }
                    connection = null;
                }
            }
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
    /// Represents a lock held via an exclusive RabbitMQ queue.
    /// </summary>
    internal class RabbitMqDistributedLock : IDistributedLock
    {
        private readonly ILogger logger;
        private IModel? channel;
        private readonly string queueName;
        private bool disposed = false;
        private readonly object disposeLock = new object();

        public string ResourceKey { get; }

        public bool IsValid => !disposed && channel != null && channel.IsOpen;

        public RabbitMqDistributedLock(ILogger logger, IModel channel, string queueName, string resourceKey)
        {
            this.logger = logger;
            this.channel = channel;
            this.queueName = queueName;
            this.ResourceKey = resourceKey;
        }

        public Task<bool> RenewLeaseAsync(int leaseTimeMs, CancellationToken cancellationToken)
        {
            // With exclusive queues, as long as the connection is alive, the lock is held
            // The queue will auto-delete when connection closes, so no explicit renewal needed
            // Just verify the channel is still open
            return Task.FromResult(IsValid);
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
                        // Delete the queue to release the lock immediately
                        try
                        {
                            channel.QueueDelete(queueName);
                            logger.LogDebug("Released distributed lock for resource '{ResourceKey}'", ResourceKey);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Error deleting lock queue for '{ResourceKey}'", ResourceKey);
                        }

                        channel.Close();
                        channel.Dispose();
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

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

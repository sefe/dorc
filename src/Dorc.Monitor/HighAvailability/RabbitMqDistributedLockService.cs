using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Client.OAuth2;
using System.Security.Authentication;

namespace Dorc.Monitor.HighAvailability
{
    /// <summary>
    /// RabbitMQ-based distributed lock service using quorum queues with single-active consumer pattern.
    /// Supports RabbitMQ clusters with automatic failover and ensures only one monitor instance processes deployments per environment.
    /// Uses OAuth 2.0 client credentials flow with automatic token refresh via official RabbitMQ.Client.OAuth2 package.
    /// </summary>
    public class RabbitMqDistributedLockService : IDistributedLockService, IDisposable
    {
        private readonly ILogger<RabbitMqDistributedLockService> logger;
        private readonly IMonitorConfiguration configuration;
        // HttpClient instance is managed by IHttpClientFactory and must NOT be disposed manually.
        private IConnection? connection;
        private readonly SemaphoreSlim connectionSemaphore = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource serviceCts = new CancellationTokenSource();
        private HttpClientHandler? httpClientHandler;
        private OAuth2ClientCredentialsProvider? credentialsProvider;
        private bool disposed = false;
        private int connectionGeneration = 0; // Tracks connection refresh cycles to prevent redundant refreshes
        private DateTime? tokenExpiryTimeUtc; // Tracks when the current OAuth token expires
        private static readonly TimeSpan TokenRefreshThreshold = TimeSpan.FromMinutes(5); // Refresh token 5 minutes before expiry

        public RabbitMqDistributedLockService(
            ILogger<RabbitMqDistributedLockService> logger,
            IMonitorConfiguration configuration)
        {
            this.logger = logger;
            this.configuration = configuration;
        }

        public bool IsEnabled => configuration.HighAvailabilityEnabled;

        /// <summary>
        /// Checks if the OAuth token is expiring soon (within the refresh threshold).
        /// </summary>
        private bool IsTokenExpiringSoon()
        {
            if (!tokenExpiryTimeUtc.HasValue)
                return false;

            var timeUntilExpiry = tokenExpiryTimeUtc.Value - DateTime.UtcNow;
            return timeUntilExpiry <= TokenRefreshThreshold;
        }

        public async Task<IDistributedLock?> TryAcquireLockAsync(string resourceKey, int leaseTimeMs, CancellationToken cancellationToken)
        {
            if (!IsEnabled)
            {
                logger.LogDebug("Distributed locking is disabled, returning null lock for resource '{ResourceKey}'", resourceKey);
                return null;
            }

            // Proactively refresh token if it's expiring soon (within 5 minutes)
            // This prevents ACCESS_REFUSED errors during lock acquisition
            if (IsTokenExpiringSoon())
            {
                var timeUntilExpiry = tokenExpiryTimeUtc!.Value - DateTime.UtcNow;
                logger.LogInformation("OAuth token expiring in {Minutes:F1} minutes - proactively refreshing connection before lock acquisition for '{ResourceKey}'",
                    timeUntilExpiry.TotalMinutes, resourceKey);
                await ForceConnectionRefreshAsync(connectionGeneration, cancellationToken);
            }

            // Try lock acquisition with retry on token expiry
            const int maxRetries = 2;
            for (int retry = 0; retry < maxRetries; retry++)
            {
                // Capture current connection generation before attempting lock acquisition
                // This allows us to detect if another thread already refreshed the connection
                int currentGeneration = connectionGeneration;
                
                try
                {
                    await EnsureConnectionAsync(cancellationToken);
                    
                    if (connection == null || !connection.IsOpen)
                    {
                        logger.LogWarning("RabbitMQ connection is not available for lock acquisition on '{ResourceKey}'", resourceKey);
                        return null;
                    }

                    var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                    
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
                            autoDelete: false,
                            cancellationToken: cancellationToken);

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
                            arguments: args,
                            cancellationToken: cancellationToken);

                        // Bind queue to environment-specific exchange
                        await channel.QueueBindAsync(
                            queue: queueName,
                            exchange: exchangeName,
                            routingKey: queueName,
                            cancellationToken: cancellationToken);

                        // Start consuming BEFORE checking message count and publishing
                        // This ensures we receive any messages published after we start consuming
                        var consumer = new AsyncEventingBasicConsumer(channel);
                        var lockAcquired = new TaskCompletionSource<bool>();
                        
                        // Attach event handler to receive and hold the message
                        consumer.ReceivedAsync += async (model, ea) =>
                        {
                            // Message received - lock is held by NOT acknowledging
                            // The lock will be released when consumer is cancelled (in Dispose)
                            lockAcquired.TrySetResult(true);
                            await Task.CompletedTask;
                        };
                        
                        var consumerTag = await channel.BasicConsumeAsync(
                            queue: queueName,
                            autoAck: false, // Manual ack - lock held until we ack or consumer disconnects
                            consumer: consumer,
                            cancellationToken: cancellationToken);

                        // Check if lock queue already has a message (lock is held by another monitor)
                        // Check AFTER setting up consumer to avoid race where message is published before consumer exists
                        var queueInfo = await channel.QueueDeclarePassiveAsync(queue: queueName, cancellationToken: cancellationToken);
                        
                        // If queue has messages, another monitor holds the lock
                        if (queueInfo.MessageCount > 0)
                        {
                            logger.LogDebug("Lock for resource '{ResourceKey}' is already held (queue has {MessageCount} messages). Cannot acquire lock.", 
                                resourceKey, queueInfo.MessageCount);
                            await channel.BasicCancelAsync(consumerTag, cancellationToken: cancellationToken);
                            await channel.CloseAsync(cancellationToken: cancellationToken);
                            await channel.DisposeAsync();
                            return null;
                        }

                        // Publish a lock message that the consumer will hold
                        // Consumer is already set up, so it will receive this message
                        var lockMessage = System.Text.Encoding.UTF8.GetBytes($"lock:{resourceKey}:{DateTime.UtcNow:O}");
                        await channel.BasicPublishAsync(
                            exchange: exchangeName,
                            routingKey: queueName,
                            body: lockMessage,
                            cancellationToken: cancellationToken);

                        // Wait for lock message to be delivered (with timeout)
                        using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                        {
                            try
                            {
                                await lockAcquired.Task.WaitAsync(linkedCts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                logger.LogWarning("Timeout waiting for lock message on '{ResourceKey}'. Another consumer may have claimed it.", resourceKey);
                                await channel.BasicCancelAsync(consumerTag, cancellationToken: CancellationToken.None);
                                await channel.CloseAsync(cancellationToken: CancellationToken.None);
                                await channel.DisposeAsync();
                                return null;
                            }
                        }

                        logger.LogDebug("Successfully acquired distributed lock for resource '{ResourceKey}' using quorum queue with single-active consumer", 
                            resourceKey);

                        return new RabbitMqDistributedLock(logger, channel, queueName, resourceKey, consumerTag);
                    }
                    catch (OperationInterruptedException opEx) when (opEx.Message.Contains("ACCESS_REFUSED") && retry < maxRetries - 1)
                    {
                        // OAuth token likely expired - close channel and force connection refresh
                        logger.LogWarning(opEx, "ACCESS_REFUSED error on lock acquisition for '{ResourceKey}' - OAuth token may have expired. Refreshing connection and retrying (attempt {Retry}/{MaxRetries})", 
                            resourceKey, retry + 1, maxRetries);
                        
                        await channel.CloseAsync(cancellationToken: cancellationToken);
                        await channel.DisposeAsync();
                        
                        // Force connection recreation to get fresh OAuth token
                        // Pass the generation we captured before the attempt to prevent redundant refreshes
                        await ForceConnectionRefreshAsync(currentGeneration, cancellationToken);
                        
                        // Continue to next retry iteration
                        continue;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error acquiring lock for '{ResourceKey}'", resourceKey);
                        await channel.CloseAsync(cancellationToken: cancellationToken);
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
            
            logger.LogError("Failed to acquire distributed lock for '{ResourceKey}' after {MaxRetries} retries", resourceKey, maxRetries);
            return null;
        }

        private async Task ForceConnectionRefreshAsync(int expectedGeneration, CancellationToken cancellationToken)
        {
            await connectionSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Check if another thread already refreshed the connection while we were waiting
                // If the generation has changed, another thread beat us to the refresh
                if (connectionGeneration != expectedGeneration)
                {
                    logger.LogDebug("Connection was already refreshed by another thread (expected generation {ExpectedGeneration}, current {CurrentGeneration}). Skipping redundant refresh.", 
                        expectedGeneration, connectionGeneration);
                    return;
                }
                
                // Close and dispose existing connection if any
                if (connection != null)
                {
                    try
                    {
                        await connection.CloseAsync(cancellationToken);
                        connection.Dispose();
                        logger.LogDebug("Closed existing RabbitMQ connection for refresh");
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error closing connection during forced refresh");
                    }
                    connection = null;
                }

                // Clear cached credentials and token expiry to force new token acquisition
                credentialsProvider = null;
                tokenExpiryTimeUtc = null;

                // Increment generation to indicate a new connection cycle
                connectionGeneration++;
                
                logger.LogInformation("Forced RabbitMQ connection refresh to obtain new OAuth token (generation {Generation})", connectionGeneration);
            }
            finally
            {
                connectionSemaphore.Release();
            }
            
            // Recreate connection with new credentials
            await EnsureConnectionAsync(cancellationToken);
        }

        private async Task EnsureConnectionAsync(CancellationToken cancellationToken)
        {
            await connectionSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (connection != null && connection.IsOpen)
                    return;

                if (!IsEnabled)
                    return;

                try
                {
                    var factory = new ConnectionFactory
                    {
                        HostName = configuration.RabbitMqHostName,
                        Port = configuration.RabbitMqPort,
                        VirtualHost = configuration.RabbitMqVirtualHost ?? "/",
                        RequestedHeartbeat = TimeSpan.FromSeconds(60),
                        AutomaticRecoveryEnabled = true,
                        NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
                        ClientProvidedName = $"DOrc.Monitor-{Environment.MachineName}-{Guid.NewGuid()}",
                        UserName = "",
                        Password = ""
                    };

                    // Configure OAuth 2.0 with automatic token refresh
                    await ConfigureOAuth2Async(factory, cancellationToken);

                    // Configure SSL/TLS for RabbitMQ connection if enabled
                    if (configuration.RabbitMqSslEnabled)
                    {
                        var sslProtocols = GetSslProtocols();
                        factory.Ssl = new SslOption
                        {
                            Enabled = true,
                            ServerName = configuration.RabbitMqSslServerName,
                            AcceptablePolicyErrors = System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch |
                                                     System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors,
                            Version = sslProtocols
                        };
                        logger.LogDebug("RabbitMQ SSL/TLS enabled with version {Version} and server name: {ServerName}", 
                            sslProtocols, configuration.RabbitMqSslServerName);
                    }

                    connection = await factory.CreateConnectionAsync(cancellationToken);
                    logger.LogInformation("Established RabbitMQ connection to {HostName}:{Port} with OAuth 2.0 automatic token refresh{SslInfo}", 
                        configuration.RabbitMqHostName, 
                        configuration.RabbitMqPort,
                        configuration.RabbitMqSslEnabled ? " and SSL/TLS" : "");
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

        private async Task ConfigureOAuth2Async(ConnectionFactory factory, CancellationToken cancellationToken)
        {
            logger.LogDebug("Configuring OAuth 2.0 with automatic token refresh for RabbitMQ");

            var tokenEndpointUri = new Uri(configuration.RabbitMqOAuthTokenEndpoint);
            var handler = CreateHttpClientHandler();

            // Create OAuth2 client with credentials using v2.0.0 API
            var oAuth2ClientBuilder = new OAuth2ClientBuilder(
                configuration.RabbitMqOAuthClientId,
                configuration.RabbitMqOAuthClientSecret,
                tokenEndpointUri);

            oAuth2ClientBuilder.SetHttpClientHandler(handler);

            // Add scope if provided
            if (!string.IsNullOrWhiteSpace(configuration.RabbitMqOAuthScope))
            {
                oAuth2ClientBuilder.SetScope(configuration.RabbitMqOAuthScope);
                logger.LogDebug("OAuth 2.0 scope configured: {Scope}", configuration.RabbitMqOAuthScope);
            }

            // Build the OAuth2 client (async)
            var oAuth2Client = await oAuth2ClientBuilder.BuildAsync(cancellationToken);

            // Create credentials provider - this handles OAuth token acquisition and caching
            // Note: The RabbitMQ PLAIN mechanism uses the UserName/Password set on the ConnectionFactory,
            // along with the CredentialsProvider. The CredentialsProvider intercepts the PLAIN challenge
            // and provides the OAuth token as the password. The UserName must match what RabbitMQ expects.
            credentialsProvider = new OAuth2ClientCredentialsProvider("DOrc", oAuth2Client);

            // Get initial token to ensure credentials are available before first connection attempt
            // This primes the credentials provider so it has a valid token ready
            var initialCredentials = await credentialsProvider.GetCredentialsAsync(cancellationToken);

            // Track token expiry time for proactive refresh
            if (initialCredentials.ValidUntil.HasValue)
            {
                tokenExpiryTimeUtc = DateTime.UtcNow.Add(initialCredentials.ValidUntil.Value);
            }

            logger.LogInformation("OAuth 2.0 token automatically refreshed. Valid for {Days} days, {Hours} hours, {Minutes} minutes, {Seconds} seconds (expires at {ExpiryTime:O})",
                initialCredentials.ValidUntil?.Days ?? 0,
                initialCredentials.ValidUntil?.Hours ?? 0,
                initialCredentials.ValidUntil?.Minutes ?? 0,
                initialCredentials.ValidUntil?.Seconds ?? 0,
                tokenExpiryTimeUtc);

            // Configure connection factory with OAuth credentials provider
            // The CredentialsProvider will be called by RabbitMQ.Client for each connection attempt
            // It automatically returns the cached token if still valid, or acquires a new one if expired
            factory.CredentialsProvider = credentialsProvider;

            logger.LogDebug("OAuth 2.0 credentials provider configured for RabbitMQ");
        }

        private HttpClientHandler CreateHttpClientHandler()
        {
            // Dispose previous handler if it exists
            httpClientHandler?.Dispose();
            
            var handler = new HttpClientHandler();
            httpClientHandler = handler;

            // If SSL is enabled for RabbitMQ, use appropriate certificate handling for token endpoint
            if (configuration.RabbitMqSslEnabled)
            {
                var sslProtocols = GetSslProtocols();
                handler.SslProtocols = sslProtocols;
                logger.LogDebug("HTTP client SSL/TLS version configured for OAuth token endpoint: {Version}", sslProtocols);

                // Accept self-signed or mismatched certificates if server name is specified
                if (!string.IsNullOrWhiteSpace(configuration.RabbitMqSslServerName))
                {
                    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                    {
                        if (errors == System.Net.Security.SslPolicyErrors.None)
                            return true;

                        logger.LogWarning("SSL certificate validation error for OAuth token endpoint: {Errors}. Accepting certificate due to configuration.", errors);
                        return true; // Accept self-signed or mismatched certificates as configured
                    };
                }
            }

            return handler;
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

        /// <summary>
        /// Parses the configured TLS version string to System.Security.Authentication.SslProtocols.
        /// Supported values: Tls12, Tls13, TLS12_TLS13, or empty for system default.
        /// </summary>
        private SslProtocols GetSslProtocols()
        {
            if (string.IsNullOrWhiteSpace(configuration.RabbitMqSslVersion))
            {
                // Default to TLS 1.2+ (system will negotiate highest supported)
                return SslProtocols.Tls12 | SslProtocols.Tls13;
            }

            return configuration.RabbitMqSslVersion.ToUpperInvariant() switch
            {
                "TLS12" => SslProtocols.Tls12,
                "TLS13" => SslProtocols.Tls13,
                "TLS12_TLS13" or "TLS1.2_TLS1.3" => SslProtocols.Tls12 | SslProtocols.Tls13,
                _ => SslProtocols.Tls12 | SslProtocols.Tls13
            };
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            try
            {
                // Cancel service-level operations
                serviceCts?.Cancel();
                serviceCts?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error cancelling service operations during disposal");
            }

            IConnection? connToDispose = null;
            connectionSemaphore.Wait(TimeSpan.FromSeconds(5));
            try
            {
                connToDispose = connection;
                connection = null;
            }
            finally
            {
                connectionSemaphore.Release();
            }

            if (connToDispose != null)
            {
                try
                {
                    connToDispose.CloseAsync().Wait(TimeSpan.FromSeconds(5));
                    connToDispose.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error disposing RabbitMQ connection");
                }
            }

            try
            {
                httpClientHandler?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error disposing HTTP client handler");
            }

            connectionSemaphore.Dispose();
        }
    }

    /// <summary>
    /// Represents a distributed lock held via RabbitMQ single-active consumer on a quorum queue.
    /// </summary>
    internal class RabbitMqDistributedLock : IDistributedLock
    {
        private readonly ILogger logger;
        private readonly IChannel channel;
        private readonly string queueName;
        private readonly string resourceKey;
        private readonly string consumerTag;
        private int disposedFlag = 0;  // Use int for Interlocked operations

        public string ResourceKey => resourceKey;

        public RabbitMqDistributedLock(ILogger logger, IChannel channel, string queueName, string resourceKey, string consumerTag)
        {
            this.logger = logger;
            this.channel = channel;
            this.queueName = queueName;
            this.resourceKey = resourceKey;
            this.consumerTag = consumerTag;
        }

        public async ValueTask DisposeAsync()
        {
            // Thread-safe disposal check and set using Interlocked
            if (Interlocked.Exchange(ref disposedFlag, 1) == 1)
                return;

            try
            {
                // Cancel the consumer to release the lock
                await channel.BasicCancelAsync(consumerTag);
                logger.LogDebug("Cancelled consumer for lock '{ResourceKey}'", resourceKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error cancelling consumer for lock '{ResourceKey}'", resourceKey);
            }

            try
            {
                // Purge all messages from the queue to clean up lock tokens
                await channel.QueuePurgeAsync(queueName);
                logger.LogDebug("Purged messages from lock queue '{QueueName}' for resource '{ResourceKey}'", queueName, resourceKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error purging lock queue '{QueueName}' for resource '{ResourceKey}'", queueName, resourceKey);
            }

            try
            {
                // Delete the queue to clean up after deployment completes
                // This prevents accumulation of unused lock queues
                await channel.QueueDeleteAsync(queue: queueName, ifUnused: false, ifEmpty: false);
                logger.LogInformation("Deleted lock queue '{QueueName}' for resource '{ResourceKey}' after deployment completed", queueName, resourceKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error deleting lock queue '{QueueName}' for resource '{ResourceKey}'", queueName, resourceKey);
            }

            try
            {
                // Close the channel
                await channel.CloseAsync();
                await channel.DisposeAsync();
                logger.LogDebug("Closed channel for lock '{ResourceKey}'", resourceKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error closing channel for lock '{ResourceKey}'", resourceKey);
            }
        }

        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }
    }
}

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
    public class RabbitMqDistributedLockService : IDistributedLockService, IAsyncDisposable, IDisposable
    {
        private readonly ILogger<RabbitMqDistributedLockService> logger;
        private readonly IMonitorConfiguration configuration;
        // HttpClient instance is managed by IHttpClientFactory and must NOT be disposed manually.
        private IConnection? connection;
        private readonly SemaphoreSlim connectionSemaphore = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource serviceCts = new CancellationTokenSource();
        private HttpClientHandler? httpClientHandler;
        private OAuth2ClientCredentialsProvider? credentialsProvider;
        private volatile bool disposed = false;
        private Timer? tokenRefreshTimer;
        private readonly List<(IConnection Connection, DateTime RetiredAt)> _retiredConnections = new();
        private static readonly TimeSpan RetiredConnectionGracePeriod = TimeSpan.FromMinutes(2);
        private int connectionGeneration = 0; // Tracks connection refresh cycles to prevent redundant refreshes
        // tokenExpiryTimeUtc is read outside the semaphore in IsTokenExpiringSoon() but only
        // written under connectionSemaphore. Stale reads are harmless: a slightly stale value
        // may cause an unnecessary proactive refresh (safe) or delay refresh by one cycle (also safe,
        // since the background timer will catch it).
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

        private void StartTokenRefreshTimer()
        {
            tokenRefreshTimer?.Dispose();
            var interval = TimeSpan.FromMinutes(configuration.OAuthTokenRefreshCheckIntervalMinutes);
            tokenRefreshTimer = new Timer(_ =>
            {
                if (disposed) return;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (IsTokenExpiringSoon())
                        {
                            var currentGeneration = connectionGeneration;
                            logger.LogInformation("Background token refresh timer detected token expiring soon - refreshing connection (generation {Generation})", currentGeneration);
                            await ForceConnectionRefreshAsync(currentGeneration, serviceCts.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Background token refresh timer encountered an error");
                    }
                }, serviceCts.Token);
            }, null, interval, interval);
            logger.LogDebug("Background OAuth token refresh timer started with interval {Interval} minutes", configuration.OAuthTokenRefreshCheckIntervalMinutes);
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

                    // Capture a local reference to the connection. Another thread may call
                    // ForceConnectionRefreshAsync between our null-check and CreateChannelAsync,
                    // setting the field to null. A local reference avoids that race.
                    var conn = connection;
                    if (conn == null || !conn.IsOpen)
                    {
                        logger.LogWarning("RabbitMQ connection is not available for lock acquisition on '{ResourceKey}'", resourceKey);
                        return null;
                    }

                    var channel = await conn.CreateChannelAsync(cancellationToken: cancellationToken);
                    
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

                        // Publish a lock message that the consumer will hold.
                        // Per-message TTL ensures crash recovery: if the monitor dies without
                        // disposing the lock, the message expires and another monitor can acquire it.
                        var lockMessage = System.Text.Encoding.UTF8.GetBytes($"lock:{resourceKey}:{DateTime.UtcNow:O}");
                        var properties = new BasicProperties
                        {
                            Expiration = leaseTimeMs.ToString()
                        };
                        await channel.BasicPublishAsync(
                            exchange: exchangeName,
                            routingKey: queueName,
                            mandatory: false,
                            basicProperties: properties,
                            body: lockMessage,
                            cancellationToken: cancellationToken);

                        // Wait for lock message to be delivered (with timeout)
                        using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(configuration.LockAcquisitionTimeoutSeconds)))
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

                        return new RabbitMqDistributedLock(logger, channel, queueName, resourceKey, consumerTag, this);
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
                    catch (AlreadyClosedException acEx) when (retry < maxRetries - 1)
                    {
                        logger.LogWarning(acEx, "Connection closed during lock acquisition for '{ResourceKey}' - refreshing and retrying (attempt {Retry}/{MaxRetries})",
                            resourceKey, retry + 1, maxRetries);
                        await ForceConnectionRefreshAsync(currentGeneration, cancellationToken);
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
                
                // Retire (don't close) the existing connection. In-flight lock channels may still
                // reference it; closing it immediately would kill those channels and cause
                // AlreadyClosedException in their cleanup path. Instead, we add it to a
                // retired list and only dispose connections that are already closed.
                if (connection != null)
                {
                    _retiredConnections.Add((connection, DateTime.UtcNow));
                    connection = null;
                    logger.LogDebug("Retired existing RabbitMQ connection for refresh");
                }

                CleanupRetiredConnections();

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

        /// <summary>
        /// Disposes retired connections that are already closed or older than the grace period.
        /// Must be called under connectionSemaphore.
        /// Connections are given a grace period to allow in-flight lock channels to complete
        /// their cleanup before the connection is forcibly closed.
        /// </summary>
        private void CleanupRetiredConnections()
        {
            for (int i = _retiredConnections.Count - 1; i >= 0; i--)
            {
                var (conn, retiredAt) = _retiredConnections[i];
                var shouldDispose = !conn.IsOpen || (DateTime.UtcNow - retiredAt) > RetiredConnectionGracePeriod;
                if (shouldDispose)
                {
                    try
                    {
                        if (conn.IsOpen)
                        {
                            logger.LogDebug("Force-closing retired RabbitMQ connection after grace period");
                            // CloseAsync would require async context; use sync Dispose which closes + disposes
                        }
                        conn.Dispose();
                        logger.LogDebug("Disposed retired RabbitMQ connection");
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error disposing retired RabbitMQ connection");
                    }
                    _retiredConnections.RemoveAt(i);
                }
            }
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

                    StartTokenRefreshTimer();
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
                    // SECURITY NOTE: This callback accepts certificates with validation errors.
                    // This is intentional for environments using self-signed or internal CA certificates.
                    // In production, ensure RabbitMqSslServerName is correctly configured to limit exposure.
                    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                    {
                        if (errors == System.Net.Security.SslPolicyErrors.None)
                            return true;

                        logger.LogWarning(
                            "SSL certificate validation error for OAuth token endpoint: {Errors}. " +
                            "Subject={Subject}, Issuer={Issuer}, Thumbprint={Thumbprint}. " +
                            "Accepting certificate due to configuration.",
                            errors,
                            cert?.Subject ?? "N/A",
                            cert?.Issuer ?? "N/A",
                            cert?.GetCertHashString() ?? "N/A");
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

        /// <summary>
        /// Attempts to delete a lock queue using the current connection.
        /// Called by RabbitMqDistributedLock.DisposeAsync as a fallback when the lock's
        /// original channel is dead (e.g. due to a concurrent connection refresh).
        /// This prevents orphaned lock queues that permanently block environments.
        /// </summary>
        internal async Task<bool> TryDeleteQueueAsync(string queueName, string resourceKey)
        {
            try
            {
                // Safely capture the current connection reference under the semaphore
                // to avoid a race with ForceConnectionRefreshAsync setting connection to null.
                IConnection? currentConnection = null;

                var semaphoreAcquired = await connectionSemaphore.WaitAsync(TimeSpan.FromSeconds(5));
                if (!semaphoreAcquired)
                {
                    logger.LogWarning("Timeout waiting to acquire connection semaphore when deleting orphaned lock queue '{QueueName}'", queueName);
                    return false;
                }

                try
                {
                    currentConnection = connection;
                }
                finally
                {
                    connectionSemaphore.Release();
                }

                if (currentConnection == null || !currentConnection.IsOpen)
                {
                    logger.LogWarning("Cannot delete orphaned lock queue '{QueueName}' - no active connection", queueName);
                    return false;
                }

                // Use a short timeout to prevent indefinite hangs if the broker is unhealthy
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var ct = timeoutCts.Token;

                await using var channel = await currentConnection.CreateChannelAsync(cancellationToken: ct);
                await channel.QueuePurgeAsync(queueName, ct);
                await channel.QueueDeleteAsync(queue: queueName, ifUnused: false, ifEmpty: false, cancellationToken: ct);
                await channel.CloseAsync(ct);
                logger.LogInformation("Deleted orphaned lock queue '{QueueName}' for resource '{ResourceKey}' via fallback channel", queueName, resourceKey);
                return true;
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Timeout (10s) deleting orphaned lock queue '{QueueName}' for resource '{ResourceKey}' via fallback channel", queueName, resourceKey);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete orphaned lock queue '{QueueName}' for resource '{ResourceKey}' via fallback channel", queueName, resourceKey);
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
                return;

            disposed = true;

            try
            {
                // Cancel service-level operations first so any in-flight timer callback exits quickly
                serviceCts?.Cancel();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error cancelling service operations during disposal");
            }

            try
            {
                if (tokenRefreshTimer != null)
                {
                    await tokenRefreshTimer.DisposeAsync();
                    tokenRefreshTimer = null;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error disposing token refresh timer during disposal");
            }

            try
            {
                serviceCts?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error disposing service cancellation token source during disposal");
            }

            IConnection? connToDispose = null;

            // Use async semaphore wait to avoid potential deadlocks
            var semaphoreAcquired = await connectionSemaphore.WaitAsync(TimeSpan.FromSeconds(5));
            if (!semaphoreAcquired)
            {
                logger.LogWarning("Timeout waiting to acquire connection semaphore during disposal");
            }

            try
            {
                connToDispose = connection;
                connection = null;
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    connectionSemaphore.Release();
                }
            }

            if (connToDispose != null)
            {
                // IMPORTANT: CloseAsync and Dispose must be in separate try blocks (same
                // pattern as ForceConnectionRefreshAsync). If CloseAsync throws, Dispose
                // must still run to release the underlying TCP socket and deregister consumers.
                try
                {
                    await connToDispose.CloseAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error closing RabbitMQ connection during disposal");
                }

                try
                {
                    connToDispose.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error disposing RabbitMQ connection during disposal");
                }
            }

            // Close and dispose all retired connections
            foreach (var (conn, _) in _retiredConnections)
            {
                try
                {
                    if (conn.IsOpen)
                        await conn.CloseAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error closing retired connection during disposal");
                }

                try
                {
                    conn.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error disposing retired connection during disposal");
                }
            }
            _retiredConnections.Clear();

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

        /// <remarks>
        /// WARNING: Sync-over-async. This blocks the calling thread while DisposeAsync runs.
        /// Prefer DisposeAsync where possible. This exists only for IDisposable compatibility
        /// (e.g., DI container disposal when IAsyncDisposable is not supported).
        /// </remarks>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
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
        private readonly RabbitMqDistributedLockService lockService;
        private int disposedFlag = 0;  // Use int for Interlocked operations

        public string ResourceKey => resourceKey;

        public bool IsValid => channel.IsOpen;

        public RabbitMqDistributedLock(ILogger logger, IChannel channel, string queueName, string resourceKey, string consumerTag, RabbitMqDistributedLockService lockService)
        {
            this.logger = logger;
            this.channel = channel;
            this.queueName = queueName;
            this.resourceKey = resourceKey;
            this.consumerTag = consumerTag;
            this.lockService = lockService;
        }

        public async ValueTask DisposeAsync()
        {
            // Thread-safe disposal check and set using Interlocked
            if (Interlocked.Exchange(ref disposedFlag, 1) == 1)
                return;

            bool queueDeleted = false;

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
                queueDeleted = true;
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

            // Fallback: if queue deletion failed (e.g. because the connection was refreshed
            // concurrently and our channel is dead), try to delete the queue using a fresh
            // channel from the lock service's current connection. Without this, the orphaned
            // queue retains the lock message and permanently blocks the environment.
            if (!queueDeleted)
            {
                logger.LogWarning("Lock queue '{QueueName}' for resource '{ResourceKey}' was not deleted via original channel - attempting fallback cleanup", queueName, resourceKey);
                await lockService.TryDeleteQueueAsync(queueName, resourceKey);
            }
        }

        /// <remarks>
        /// WARNING: Sync-over-async. Blocks the calling thread while DisposeAsync runs.
        /// Prefer DisposeAsync where possible.
        /// </remarks>
        public void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }
    }
}

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
        private IConnection? connection;
        private readonly SemaphoreSlim connectionSemaphore = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource serviceCts = new CancellationTokenSource();
        private HttpClientHandler? httpClientHandler;
        private bool disposed = false;

        public RabbitMqDistributedLockService(
            ILogger<RabbitMqDistributedLockService> logger,
            IMonitorConfiguration configuration)
        {
            this.logger = logger;
            this.configuration = configuration;
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

                    // Publish a lock message that the consumer will hold
                    var lockMessage = System.Text.Encoding.UTF8.GetBytes($"lock:{resourceKey}:{DateTime.UtcNow:O}");
                    await channel.BasicPublishAsync(
                        exchange: exchangeName,
                        routingKey: queueName,
                        body: lockMessage,
                        cancellationToken: cancellationToken);

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
                        consumer: consumer,
                        cancellationToken: cancellationToken);

                    logger.LogDebug("Successfully acquired distributed lock for resource '{ResourceKey}' using quorum queue with single-active consumer", 
                        resourceKey);

                    return new RabbitMqDistributedLock(logger, channel, queueName, resourceKey, consumerTag);
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
            var httpClientHandler = CreateHttpClientHandler();

            // Create OAuth2 client with credentials using v2.0.0 API
            var oAuth2ClientBuilder = new OAuth2ClientBuilder(
                configuration.RabbitMqOAuthClientId,
                configuration.RabbitMqOAuthClientSecret,
                tokenEndpointUri);

            oAuth2ClientBuilder.SetHttpClientHandler(httpClientHandler);

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
            var credentialsProvider = new OAuth2ClientCredentialsProvider("DOrc", oAuth2Client);

            // Get initial token to ensure credentials are available before first connection attempt
            // This primes the credentials provider so it has a valid token ready
            var initialCredentials = await credentialsProvider.GetCredentialsAsync(cancellationToken);
            logger.LogInformation("OAuth 2.0 token automatically refreshed. Valid for {Days} days, {Hours} hours, {Minutes} minutes, {Seconds} seconds",
                initialCredentials.ValidUntil?.Days ?? 0,
                initialCredentials.ValidUntil?.Hours ?? 0,
                initialCredentials.ValidUntil?.Minutes ?? 0,
                initialCredentials.ValidUntil?.Seconds ?? 0);

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

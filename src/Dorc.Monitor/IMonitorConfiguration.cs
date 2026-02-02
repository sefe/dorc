namespace Dorc.Monitor
{
    public interface IMonitorConfiguration
    {
        bool IsProduction { get; }
        int RequestProcessingIterationDelayMs { get; }
        string ServiceName { get; }
        string DOrcConnectionString { get; }
        string RefDataApiUrl { get; }
        bool DisableSignalR { get; }
        string Environment { get; }

        // OAuth (client credentials) settings for accessing Dorc API / SignalR
        string DorcApiClientId { get; }
        string DorcApiClientSecret { get; }
        string DorcApiScope { get; }

        // High Availability settings
        bool HighAvailabilityEnabled { get; }
        string RabbitMqHostName { get; }
        int RabbitMqPort { get; }
        string? RabbitMqVirtualHost { get; }
        
        // RabbitMQ OAuth settings (client credentials flow)
        string RabbitMqOAuthClientId { get; }
        string RabbitMqOAuthClientSecret { get; }
        string RabbitMqOAuthTokenEndpoint { get; }
        string RabbitMqOAuthScope { get; }
        // RabbitMQ SSL/TLS settings
        bool RabbitMqSslEnabled { get; }
        string? RabbitMqSslServerName { get; }
        string? RabbitMqSslVersion { get; }

        // Concurrency settings
        /// <summary>
        /// Maximum number of concurrent deployments. Set to 0 for unlimited.
        /// </summary>
        int MaxConcurrentDeployments { get; }

        /// <summary>
        /// Timeout in seconds for acquiring a distributed lock. Default: 5.
        /// </summary>
        int LockAcquisitionTimeoutSeconds { get; }

        /// <summary>
        /// Interval in minutes for background OAuth token refresh checks. Default: 15.
        /// </summary>
        int OAuthTokenRefreshCheckIntervalMinutes { get; }
    }
}

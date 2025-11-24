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
        
        // RabbitMQ OAuth settings
        string RabbitMqOAuthClientId { get; }
        string RabbitMqOAuthClientSecret { get; }
        string RabbitMqOAuthTokenEndpoint { get; }
        string RabbitMqOAuthScope { get; }
    }
}

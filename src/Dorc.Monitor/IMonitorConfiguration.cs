namespace Dorc.Monitor
{
    public interface IMonitorConfiguration
    {
        bool IsProduction { get; }
        int RequestProcessingIterationDelayMs { get; }
        string ServiceName { get; }
        string DOrcConnectionString { get; }
        string RefDataApiUrl { get; }
    }
}

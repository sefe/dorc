namespace Dorc.Core.Connectivity
{
    /// <summary>
    /// Interface for checking connectivity to servers and databases
    /// </summary>
    public interface IConnectivityChecker
    {
        /// <summary>
        /// Checks if a server is reachable
        /// </summary>
        Task<bool> CheckServerConnectivityAsync(string serverName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a database is reachable
        /// </summary>
        Task<bool> CheckDatabaseConnectivityAsync(string serverName, string databaseName, CancellationToken cancellationToken = default);
    }
}

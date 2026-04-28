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
        /// <param name="serverName">Name of the server to check</param>
        /// <returns>True if the server is reachable, false otherwise</returns>
        Task<bool> CheckServerConnectivityAsync(string serverName);

        /// <summary>
        /// Checks if a database is reachable
        /// </summary>
        /// <param name="serverName">Name of the database server</param>
        /// <param name="databaseName">Name of the database</param>
        /// <returns>True if the database is reachable, false otherwise</returns>
        Task<bool> CheckDatabaseConnectivityAsync(string serverName, string databaseName);
    }
}

using System.Data.SqlClient;
using System.Net.NetworkInformation;

namespace Dorc.Core.Connectivity
{
    /// <summary>
    /// Implementation of connectivity checker for servers and databases
    /// </summary>
    public class ConnectivityChecker : IConnectivityChecker
    {
        private const int PingTimeoutMs = 5000;
        private const int DatabaseConnectionTimeoutSeconds = 5;

        /// <summary>
        /// Checks if a server is reachable using ICMP ping
        /// </summary>
        public async Task<bool> CheckServerConnectivityAsync(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
            {
                return false;
            }

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(serverName, PingTimeoutMs);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a database is reachable by attempting to open a connection
        /// </summary>
        public async Task<bool> CheckDatabaseConnectivityAsync(string serverName, string databaseName)
        {
            if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(databaseName))
            {
                return false;
            }

            try
            {
                var connectionString = $"Server={serverName};Database={databaseName};Connection Timeout={DatabaseConnectionTimeoutSeconds};Integrated Security=true;";
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

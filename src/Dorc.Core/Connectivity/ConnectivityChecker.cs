using System.Data.SqlClient;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Dorc.Core.Connectivity
{
    public class ConnectivityChecker : IConnectivityChecker
    {
        private const int PingTimeoutMs = 5000;
        private const int TcpProbeTimeoutMs = 5000;
        private const int SmbPort = 445;
        private const int DatabaseConnectionTimeoutSeconds = 5;

        public async Task<bool> CheckServerConnectivityAsync(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
            {
                return false;
            }

            if (await TryProbeOrFalse(() => TryPingAsync(serverName, PingTimeoutMs)))
            {
                return true;
            }

            return await TryProbeOrFalse(() => TryTcpConnectAsync(serverName, SmbPort, TcpProbeTimeoutMs));
        }

        public async Task<bool> CheckDatabaseConnectivityAsync(string serverName, string databaseName)
        {
            if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(databaseName))
            {
                return false;
            }

            return await TryProbeOrFalse(() => TryOpenSqlConnectionAsync(serverName, databaseName, DatabaseConnectionTimeoutSeconds));
        }

        protected virtual async Task<bool> TryPingAsync(string serverName, int timeoutMs)
        {
            using var ping = new Ping();
            var probeTask = ping.SendPingAsync(serverName, timeoutMs);
            var completed = await Task.WhenAny(probeTask, Task.Delay(timeoutMs));
            if (completed != probeTask)
            {
                return false;
            }
            var reply = await probeTask;
            return reply.Status == IPStatus.Success;
        }

        protected virtual async Task<bool> TryTcpConnectAsync(string serverName, int port, int timeoutMs)
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(serverName, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
            if (completed != connectTask)
            {
                return false;
            }
            await connectTask;
            return client.Connected;
        }

        protected virtual async Task<bool> TryOpenSqlConnectionAsync(string serverName, string databaseName, int timeoutSeconds)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = serverName,
                InitialCatalog = databaseName,
                ConnectTimeout = timeoutSeconds,
                IntegratedSecurity = true
            };
            using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            return true;
        }

        private static async Task<bool> TryProbeOrFalse(Func<Task<bool>> probe)
        {
            try { return await probe(); }
            catch { return false; }
        }
    }
}

using Microsoft.Data.SqlClient;
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

        public async Task<bool> CheckServerConnectivityAsync(string serverName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(serverName))
            {
                return false;
            }

            if (await TryProbeOrFalse(ct => TryPingAsync(serverName, PingTimeoutMs, ct), cancellationToken))
            {
                return true;
            }

            return await TryProbeOrFalse(ct => TryTcpConnectAsync(serverName, SmbPort, TcpProbeTimeoutMs, ct), cancellationToken);
        }

        public async Task<bool> CheckDatabaseConnectivityAsync(string serverName, string databaseName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(databaseName))
            {
                return false;
            }

            return await TryProbeOrFalse(ct => TryOpenSqlConnectionAsync(serverName, databaseName, DatabaseConnectionTimeoutSeconds, ct), cancellationToken);
        }

        protected virtual async Task<bool> TryPingAsync(string serverName, int timeoutMs, CancellationToken cancellationToken)
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(serverName, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken: cancellationToken);
            return reply.Status == IPStatus.Success;
        }

        protected virtual async Task<bool> TryTcpConnectAsync(string serverName, int port, int timeoutMs, CancellationToken cancellationToken)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(serverName, port, cancellationToken).AsTask().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);
            return client.Connected;
        }

        protected virtual async Task<bool> TryOpenSqlConnectionAsync(string serverName, string databaseName, int timeoutSeconds, CancellationToken cancellationToken)
        {
            // Encrypt=false preserves the reachability-probe semantics from the previous
            // System.Data.SqlClient implementation; Microsoft.Data.SqlClient flipped the
            // default to true, which would fail probes against servers without TLS certs.
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = serverName,
                InitialCatalog = databaseName,
                ConnectTimeout = timeoutSeconds,
                IntegratedSecurity = true,
                Encrypt = false
            };
            using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            return true;
        }

        private static async Task<bool> TryProbeOrFalse(Func<CancellationToken, Task<bool>> probe, CancellationToken cancellationToken)
        {
            try
            {
                return await probe(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }
    }
}

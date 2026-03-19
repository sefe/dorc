using Dorc.Core.Connectivity;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dorc.Monitor.Connectivity
{
    public class ConnectivityCheckService : IHostedService, IDisposable
    {
        private readonly ILog _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConnectivityChecker _connectivityChecker;
        private readonly IMonitorConfiguration _configuration;
        private System.Threading.Timer? _timer;
        private int _isCheckRunning = 0;

        public ConnectivityCheckService(
            ILog logger,
            IServiceProvider serviceProvider,
            IConnectivityChecker connectivityChecker,
            IMonitorConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _connectivityChecker = connectivityChecker;
            _configuration = configuration;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_configuration.EnableConnectivityCheck)
            {
                _logger.Info("Connectivity Check Service is disabled in configuration.");
                return Task.CompletedTask;
            }

            var intervalMinutes = _configuration.ConnectivityCheckIntervalMinutes;
            var interval = TimeSpan.FromMinutes(intervalMinutes);
            var initialDelay = TimeSpan.FromSeconds(30);

            _logger.Info($"Connectivity Check Service is starting. Check interval: {intervalMinutes} minutes.");
            _logger.Info($"Waiting {initialDelay.TotalSeconds} seconds before first connectivity check...");

            _timer = new System.Threading.Timer(RunCheckCycle, null, initialDelay, interval);

            return Task.CompletedTask;
        }

        private void RunCheckCycle(object? state)
        {
            if (Interlocked.CompareExchange(ref _isCheckRunning, 1, 0) != 0)
            {
                _logger.Info("Connectivity check cycle skipped - previous cycle still running.");
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.Info("Starting connectivity check cycle...");
                    await CheckAllServersAsync();
                    await CheckAllDatabasesAsync();
                    _logger.Info("Connectivity check cycle completed.");
                }
                catch (Exception ex)
                {
                    _logger.Error("Error during connectivity check", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _isCheckRunning, 0);
                }
            });
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.Info("Connectivity Check Service is stopping.");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        private async Task CheckAllServersAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var serversPersistentSource = scope.ServiceProvider.GetRequiredService<IServersPersistentSource>();

                var totalServers = serversPersistentSource.GetTotalServerCount();
                var batchSize = 100;
                var processedCount = 0;
                var now = DateTime.UtcNow;

                _logger.Info($"Starting server connectivity check for {totalServers} servers in batches of {batchSize}...");

                for (int skip = 0; skip < totalServers; skip += batchSize)
                {
                    var servers = serversPersistentSource.GetServersForConnectivityCheckBatch(skip, batchSize);

                    foreach (var server in servers)
                    {
                        if (string.IsNullOrWhiteSpace(server.Name))
                            continue;

                        try
                        {
                            var isReachable = await _connectivityChecker.CheckServerConnectivityAsync(server.Name);
                            serversPersistentSource.UpdateServerConnectivityStatus(server.Id, isReachable, now);

                            if (!isReachable)
                            {
                                _logger.Warn($"Server {server.Name} (ID: {server.Id}) is not reachable.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"Error checking server {server.Name} (ID: {server.Id})", ex);
                            serversPersistentSource.UpdateServerConnectivityStatus(server.Id, false, now);
                        }

                        processedCount++;
                    }

                    _logger.Info($"Processed {processedCount}/{totalServers} servers...");
                }

                _logger.Info($"Completed connectivity check for {processedCount} servers.");
            }
            catch (Exception ex)
            {
                _logger.Error("Error in CheckAllServersAsync", ex);
            }
        }

        private async Task CheckAllDatabasesAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var databasesPersistentSource = scope.ServiceProvider.GetRequiredService<IDatabasesPersistentSource>();

                var totalDatabases = databasesPersistentSource.GetTotalDatabaseCount();
                var batchSize = 100;
                var processedCount = 0;
                var now = DateTime.UtcNow;

                _logger.Info($"Starting database connectivity check for {totalDatabases} databases in batches of {batchSize}...");

                for (int skip = 0; skip < totalDatabases; skip += batchSize)
                {
                    var databases = databasesPersistentSource.GetDatabasesForConnectivityCheckBatch(skip, batchSize);

                    foreach (var database in databases)
                    {
                        if (string.IsNullOrWhiteSpace(database.ServerName) || string.IsNullOrWhiteSpace(database.Name))
                            continue;

                        try
                        {
                            var isReachable = await _connectivityChecker.CheckDatabaseConnectivityAsync(database.ServerName, database.Name);
                            databasesPersistentSource.UpdateDatabaseConnectivityStatus(database.Id, isReachable, now);

                            if (!isReachable)
                            {
                                _logger.Warn($"Database {database.Name} on {database.ServerName} (ID: {database.Id}) is not reachable.");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"Error checking database {database.Name} on {database.ServerName} (ID: {database.Id})", ex);
                            databasesPersistentSource.UpdateDatabaseConnectivityStatus(database.Id, false, now);
                        }

                        processedCount++;
                    }

                    _logger.Info($"Processed {processedCount}/{totalDatabases} databases...");
                }

                _logger.Info($"Completed connectivity check for {processedCount} databases.");
            }
            catch (Exception ex)
            {
                _logger.Error("Error in CheckAllDatabasesAsync", ex);
            }
        }
    }
}

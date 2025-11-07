using Dorc.Core.Connectivity;
using Dorc.PersistentData.Sources.Interfaces;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dorc.Monitor.Connectivity
{
    public class ConnectivityCheckService : BackgroundService
    {
        private readonly ILog _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConnectivityChecker _connectivityChecker;
        private readonly IMonitorConfiguration _configuration;
        private TimeSpan _checkInterval;

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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Check if connectivity checking is enabled
            if (!_configuration.EnableConnectivityCheck)
            {
                _logger.Info("Connectivity Check Service is disabled in configuration.");
                return;
            }

            _checkInterval = TimeSpan.FromMinutes(_configuration.ConnectivityCheckIntervalMinutes);
            
            _logger.Info($"Connectivity Check Service is starting. Check interval: {_checkInterval.TotalMinutes} minutes.");

            // Add a small initial delay to allow other services to initialize
            var initialDelay = TimeSpan.FromSeconds(30);
            _logger.Info($"Waiting {initialDelay.TotalSeconds} seconds before first connectivity check...");
            
            try
            {
                await Task.Delay(initialDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.Info("Connectivity Check Service was cancelled during initial delay.");
                return;
            }

            _logger.Info("Initial delay completed. Starting connectivity checks...");

            while (!stoppingToken.IsCancellationRequested)
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

                try
                {
                    _logger.Info($"Waiting {_checkInterval.TotalMinutes} minutes until next connectivity check...");
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.Info("Connectivity Check Service is stopping.");
                    break;
                }
            }

            _logger.Info("Connectivity Check Service has stopped.");
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

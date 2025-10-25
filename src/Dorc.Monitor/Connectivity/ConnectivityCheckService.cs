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
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

        public ConnectivityCheckService(
            ILog logger,
            IServiceProvider serviceProvider,
            IConnectivityChecker connectivityChecker)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _connectivityChecker = connectivityChecker;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.Info("Connectivity Check Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckAllServersAsync();
                    await CheckAllDatabasesAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error("Error during connectivity check", ex);
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.Info("Connectivity Check Service is stopping.");
        }

        private async Task CheckAllServersAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var serversPersistentSource = scope.ServiceProvider.GetRequiredService<IServersPersistentSource>();

                var servers = serversPersistentSource.GetAllServersForConnectivityCheck();
                var now = DateTime.UtcNow;

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
                }

                _logger.Info($"Completed connectivity check for {servers.Count()} servers.");
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

                var databases = databasesPersistentSource.GetAllDatabasesForConnectivityCheck();
                var now = DateTime.UtcNow;

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
                }

                _logger.Info($"Completed connectivity check for {databases.Count()} databases.");
            }
            catch (Exception ex)
            {
                _logger.Error("Error in CheckAllDatabasesAsync", ex);
            }
        }
    }
}

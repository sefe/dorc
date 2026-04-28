using Dorc.Core.Connectivity;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dorc.Monitor.Connectivity
{
    public sealed class ConnectivityCheckService : BackgroundService
    {
        private const int InitialDelaySeconds = 30;

        private readonly ILogger<ConnectivityCheckService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConnectivityChecker _connectivityChecker;
        private readonly IMonitorConfiguration _configuration;

        public ConnectivityCheckService(
            ILogger<ConnectivityCheckService> logger,
            IServiceProvider serviceProvider,
            IConnectivityChecker connectivityChecker,
            IMonitorConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _connectivityChecker = connectivityChecker;
            _configuration = configuration;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                if (!_configuration.EnableConnectivityCheck)
                {
                    _logger.LogWarning("Connectivity Check Service is DISABLED in configuration. Service will not run.");
                    return;
                }

                var intervalMinutes = _configuration.ConnectivityCheckIntervalMinutes;
                var interval = TimeSpan.FromMinutes(intervalMinutes);
                var initialDelay = TimeSpan.FromSeconds(InitialDelaySeconds);

                try
                {
                    await Task.Delay(initialDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Connectivity Check Service cancelled during initial delay.");
                    return;
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        _logger.LogInformation("Starting connectivity check cycle...");
                        await CheckAllServersAsync();
                        await CheckAllDatabasesAsync();
                        _logger.LogInformation("Connectivity check cycle completed.");
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Connectivity check cycle cancelled.");
                        break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Error during connectivity check cycle");
                    }

                    // Wait for next cycle
                    try
                    {
                        await Task.Delay(interval, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Connectivity Check Service stopping.");
                        break;
                    }
                }

                _logger.LogInformation("Connectivity Check Service has stopped.");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "FATAL ERROR in ConnectivityCheckService.ExecuteAsync - service crashed!");
                throw;
            }
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

                _logger.LogInformation("Starting server connectivity check for {Total} servers in batches of {BatchSize}...", totalServers, batchSize);

                for (int skip = 0; skip < totalServers; skip += batchSize)
                {
                    var servers = serversPersistentSource.GetServersForConnectivityCheckBatch(skip, batchSize);

                    foreach (var server in servers.Where(s => !string.IsNullOrWhiteSpace(s.Name)))
                    {
                        try
                        {
                            var isReachable = await _connectivityChecker.CheckServerConnectivityAsync(server.Name!);
                            serversPersistentSource.UpdateServerConnectivityStatus(server.Id, isReachable, now);

                            if (!isReachable)
                            {
                                _logger.LogWarning("Server {ServerName} (ID: {ServerId}) is not reachable.", SanitizeForLog(server.Name), server.Id);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error checking server {ServerName} (ID: {ServerId})", SanitizeForLog(server.Name), server.Id);
                            serversPersistentSource.UpdateServerConnectivityStatus(server.Id, false, now);
                        }

                        processedCount++;
                    }

                    _logger.LogInformation("Processed {Processed}/{Total} servers...", processedCount, totalServers);
                }

                _logger.LogInformation("Completed connectivity check for {Processed} servers.", processedCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckAllServersAsync");
            }
        }

        private static string SanitizeForLog(string? value) =>
            value?.Replace("\r", string.Empty).Replace("\n", string.Empty) ?? string.Empty;

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

                _logger.LogInformation("Starting database connectivity check for {Total} databases in batches of {BatchSize}...", totalDatabases, batchSize);

                for (int skip = 0; skip < totalDatabases; skip += batchSize)
                {
                    var databases = databasesPersistentSource.GetDatabasesForConnectivityCheckBatch(skip, batchSize);

                    foreach (var database in databases.Where(d => !string.IsNullOrWhiteSpace(d.ServerName) && !string.IsNullOrWhiteSpace(d.Name)))
                    {
                        try
                        {
                            var isReachable = await _connectivityChecker.CheckDatabaseConnectivityAsync(database.ServerName!, database.Name!);
                            databasesPersistentSource.UpdateDatabaseConnectivityStatus(database.Id, isReachable, now);

                            if (!isReachable)
                            {
                                _logger.LogWarning("Database {DbName} on {ServerName} (ID: {DbId}) is not reachable.", SanitizeForLog(database.Name), SanitizeForLog(database.ServerName), database.Id);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error checking database {DbName} on {ServerName} (ID: {DbId})", SanitizeForLog(database.Name), SanitizeForLog(database.ServerName), database.Id);
                            databasesPersistentSource.UpdateDatabaseConnectivityStatus(database.Id, false, now);
                        }

                        processedCount++;
                    }

                    _logger.LogInformation("Processed {Processed}/{Total} databases...", processedCount, totalDatabases);
                }

                _logger.LogInformation("Completed connectivity check for {Processed} databases.", processedCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckAllDatabasesAsync");
            }
        }
    }
}

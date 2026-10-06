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
        private const int BatchSize = 100;

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
                        await CheckAllServersAsync(stoppingToken);
                        await CheckAllDatabasesAsync(stoppingToken);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogCritical(ex, "FATAL ERROR in ConnectivityCheckService.ExecuteAsync - service crashed!");
                throw;
            }
        }

        private async Task CheckAllServersAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var serversPersistentSource = scope.ServiceProvider.GetRequiredService<IServersPersistentSource>();

                var processedCount = 0;
                var lastId = 0;

                _logger.LogInformation("Starting server connectivity check in keyset batches of {BatchSize}...", BatchSize);

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var servers = serversPersistentSource.GetServersForConnectivityCheckBatchAfter(lastId, BatchSize).ToList();
                    if (servers.Count == 0)
                    {
                        break;
                    }

                    foreach (var server in servers)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (string.IsNullOrWhiteSpace(server.Name))
                        {
                            processedCount++;
                            continue;
                        }

                        bool isReachable;
                        try
                        {
                            isReachable = await _connectivityChecker.CheckServerConnectivityAsync(server.Name!, cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Error checking server {ServerName} (ID: {ServerId})", SanitizeForLog(server.Name), server.Id);
                            isReachable = false;
                        }

                        // Capture the timestamp per-item so LastChecked reflects when this row was probed,
                        // not when the cycle started. The DB write sits outside the probe try/catch so a
                        // probe-success + DB-throw cannot silently overwrite a reachable row with isReachable=false.
                        // It has its own try/catch so a single row's transient SQL failure logs and continues
                        // to the next row instead of aborting the rest of this batch and every later batch
                        // in the cycle (the foreach unwind would otherwise leave lastId unadvanced and lose
                        // up to BatchSize-1 already-completed probes).
                        try
                        {
                            serversPersistentSource.UpdateServerConnectivityStatus(server.Id, isReachable, DateTime.UtcNow);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Failed to persist connectivity status for server {ServerName} (ID: {ServerId})", SanitizeForLog(server.Name), server.Id);
                        }

                        if (!isReachable)
                        {
                            _logger.LogWarning("Server {ServerName} (ID: {ServerId}) is not reachable.", SanitizeForLog(server.Name), server.Id);
                        }

                        processedCount++;
                    }

                    lastId = servers[^1].Id;
                    _logger.LogInformation("Processed {Processed} servers (lastId={LastId})...", processedCount, lastId);

                    if (servers.Count < BatchSize)
                    {
                        break;
                    }
                }

                _logger.LogInformation("Completed connectivity check for {Processed} servers.", processedCount);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in CheckAllServersAsync");
            }
        }

        private async Task CheckAllDatabasesAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var databasesPersistentSource = scope.ServiceProvider.GetRequiredService<IDatabasesPersistentSource>();

                var processedCount = 0;
                var lastId = 0;

                _logger.LogInformation("Starting database connectivity check in keyset batches of {BatchSize}...", BatchSize);

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var databases = databasesPersistentSource.GetDatabasesForConnectivityCheckBatchAfter(lastId, BatchSize).ToList();
                    if (databases.Count == 0)
                    {
                        break;
                    }

                    foreach (var database in databases)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (string.IsNullOrWhiteSpace(database.ServerName) || string.IsNullOrWhiteSpace(database.Name))
                        {
                            processedCount++;
                            continue;
                        }

                        bool isReachable;
                        try
                        {
                            isReachable = await _connectivityChecker.CheckDatabaseConnectivityAsync(database.ServerName!, database.Name!, cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Error checking database {DbName} on {ServerName} (ID: {DbId})", SanitizeForLog(database.Name), SanitizeForLog(database.ServerName), database.Id);
                            isReachable = false;
                        }

                        // See server-loop comment above for the rationale on per-item timestamps,
                        // splitting the DB write out of the probe try/catch, and the per-row
                        // try/catch around the persistence call.
                        try
                        {
                            databasesPersistentSource.UpdateDatabaseConnectivityStatus(database.Id, isReachable, DateTime.UtcNow);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger.LogError(ex, "Failed to persist connectivity status for database {DbName} on {ServerName} (ID: {DbId})", SanitizeForLog(database.Name), SanitizeForLog(database.ServerName), database.Id);
                        }

                        if (!isReachable)
                        {
                            _logger.LogWarning("Database {DbName} on {ServerName} (ID: {DbId}) is not reachable.", SanitizeForLog(database.Name), SanitizeForLog(database.ServerName), database.Id);
                        }

                        processedCount++;
                    }

                    lastId = databases[^1].Id;
                    _logger.LogInformation("Processed {Processed} databases (lastId={LastId})...", processedCount, lastId);

                    if (databases.Count < BatchSize)
                    {
                        break;
                    }
                }

                _logger.LogInformation("Completed connectivity check for {Processed} databases.", processedCount);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in CheckAllDatabasesAsync");
            }
        }

        private static string SanitizeForLog(string? value) =>
            value?.Replace("\r", string.Empty).Replace("\n", string.Empty) ?? string.Empty;
    }
}

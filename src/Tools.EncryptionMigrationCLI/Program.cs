using Dorc.Core.VariableResolution;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;


namespace Tools.EncryptionMigrationCLI
{
    internal class Program
    {
        private readonly ILogger _log;

        static int Main(string[] args)
        {
            try
            {
                _log.LogInformation("Starting encryption migration tool...");

                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                var connectionString = configuration.GetConnectionString("DOrcConnectionString");
                if (string.IsNullOrEmpty(connectionString))
                {
                    _log.LogError("Connection string 'DOrcConnectionString' not found in configuration");
                    return 1;
                }

                var contextFactory = new DeploymentContextFactory(connectionString);
                var secureKeySource = new SecureKeyPersistentDataSource(contextFactory);
                
                var encryptor = new QuantumResistantPropertyEncryptor(
                    secureKeySource.GetInitialisationVector(),
                    secureKeySource.GetSymmetricKey());

                int batchSize = int.TryParse(configuration["Migration:BatchSize"], out int configuredBatchSize) 
                    ? configuredBatchSize 
                    : 100;
                var migrator = new EncryptionMigration(contextFactory, encryptor, Log, batchSize);
                
                bool dryRun = args.Contains("--dry-run") || args.Contains("-d");
                bool force = args.Contains("--force") || args.Contains("-f");

                if (dryRun)
                {
                    _log.LogInformation("Running in DRY RUN mode - no changes will be made");
                }

                _log.LogInformation($"Using batch size: {batchSize}");
                var result = migrator.MigratePropertyValues(dryRun, force);

                _log.LogInformation($"Migration completed. Total: {result.Total}, Migrated: {result.Migrated}, Skipped: {result.Skipped}, Failed: {result.Failed}");

                return result.Failed > 0 ? 1 : 0;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException))
            {
                _log.LogError(ex, "Migration failed");
                return 1;
            }
        }
    }

    public class EncryptionMigration
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly QuantumResistantPropertyEncryptor _encryptor;
        private readonly ILogger _log;
        private readonly int _batchSize;

        public EncryptionMigration(IDeploymentContextFactory contextFactory, QuantumResistantPropertyEncryptor encryptor, ILogger log, int batchSize = 100)
        {
            _contextFactory = contextFactory;
            _encryptor = encryptor;
            _log = log;
            _batchSize = batchSize;
        }

        public MigrationResult MigratePropertyValues(bool dryRun, bool force)
        {
            var result = new MigrationResult();

            using (var context = _contextFactory.GetContext())
            {
                var secureProperties = context.Properties
                    .Where(p => p.Secure)
                    .ToList();

                _log.LogInformation($"Found {secureProperties.Count} secure properties");

                foreach (var property in secureProperties)
                {
                    var propertyValues = context.PropertyValues
                        .Where(pv => pv.Property.Id == property.Id && pv.Value != null)
                        .ToList();

                    _log.LogInformation($"Processing property '{property.Name}' with {propertyValues.Count} values");

                    int batchCount = 0;

                    foreach (var propertyValue in propertyValues)
                    {
                        result.Total++;

                        try
                        {
                            if (propertyValue.Value.StartsWith("v2:"))
                            {
                                _log.LogDebug($"Property value {propertyValue.Id} already migrated to v2");
                                result.Skipped++;
                                continue;
                            }

                            if (!force && propertyValue.Value.StartsWith("v1:"))
                            {
                                _log.LogDebug($"Property value {propertyValue.Id} is v1 format but not forced");
                                result.Skipped++;
                                continue;
                            }

                            _log.LogInformation($"Migrating property value {propertyValue.Id}");

                            var migratedValue = _encryptor.MigrateFromLegacy(propertyValue.Value);

                            if (!dryRun)
                            {
                                propertyValue.Value = migratedValue;
                                batchCount++;

                                if (batchCount >= _batchSize)
                                {
                                    context.SaveChanges();
                                    batchCount = 0;
                                    _log.LogInformation($"Saved batch of {_batchSize} updates");
                                }
                            }

                            result.Migrated++;
                        }
                        catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException))
                        {
                            _log.LogError(ex, $"Failed to migrate property value {propertyValue.Id}");
                            result.Failed++;
                        }
                    }

                    if (!dryRun && batchCount > 0)
                    {
                        context.SaveChanges();
                        _log.LogInformation($"Saved final batch of {batchCount} updates");
                    }
                }
            }

            return result;
        }
    }

    public class MigrationResult
    {
        public int Total { get; set; }
        public int Migrated { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
    }
}

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.Terraform
{
    public sealed class TerraformRegistrationOutcome
    {
        public string Status { get; set; } = "Unavailable";
        public bool SnapshotComplete { get; set; }
        public int RegisteredCount { get; set; }
        public int FailedCount { get; set; }
        public int EnvironmentId { get; set; }
        public List<TerraformInventoryObservation> Observed { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    /// <summary>
    /// Registers the cloud resources a successful terraform apply created (as
    /// reported by the runner's applied-resources file) against the target
    /// environment's Cloud tab, with the same audit trail the API writes for
    /// manual edits. Registration is a best-effort byproduct of the apply: the
    /// infrastructure already exists, so every failure here is logged and
    /// swallowed rather than failing a completed deployment.
    /// </summary>
    public class TerraformCloudResourceRegistrar
    {
        private readonly ILogger logger;
        private readonly ICloudResourcesPersistentSource _cloudResourcesPersistentSource;
        private readonly ICloudResourceAuditPersistentSource _cloudResourceAuditPersistentSource;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly IDatabasesPersistentSource _databasesPersistentSource;

        public TerraformCloudResourceRegistrar(
            ILogger logger,
            ICloudResourcesPersistentSource cloudResourcesPersistentSource,
            ICloudResourceAuditPersistentSource cloudResourceAuditPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            IDatabasesPersistentSource databasesPersistentSource)
        {
            this.logger = logger;
            _cloudResourcesPersistentSource = cloudResourcesPersistentSource;
            _cloudResourceAuditPersistentSource = cloudResourceAuditPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
            _databasesPersistentSource = databasesPersistentSource;
        }

        public TerraformRegistrationOutcome RegisterAppliedResources(string appliedResourcesFilePath, string environmentName, string username,
            int? deploymentResultId = null)
        {
            if (string.IsNullOrWhiteSpace(appliedResourcesFilePath) || !File.Exists(appliedResourcesFilePath))
                return new TerraformRegistrationOutcome();
            try
            {
                return RegisterSnapshot(File.ReadAllText(appliedResourcesFilePath), environmentName, username, deploymentResultId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read Terraform inventory snapshot.");
                return new TerraformRegistrationOutcome { Status = "Failed", FailedCount = 1 };
            }
        }

        public TerraformRegistrationOutcome RegisterSnapshot(string json, string environmentName, string username,
            int? deploymentResultId = null)
        {
            var result = new TerraformRegistrationOutcome();
            try
            {
                using var document = JsonDocument.Parse(json);
                IReadOnlyList<CloudResourceApiModel> resources;
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                    resources = JsonSerializer.Deserialize<List<CloudResourceApiModel>>(json)!;
                else
                {
                    var snapshot = JsonSerializer.Deserialize<TerraformInventorySnapshot>(json)
                        ?? throw new InvalidDataException("Inventory snapshot is missing.");
                    if (deploymentResultId != snapshot.DeploymentResultId)
                        throw new InvalidDataException("Inventory snapshot belongs to a different deployment result.");
                    resources = snapshot.Resources ?? throw new InvalidDataException("Inventory resources are missing.");
                    result.SnapshotComplete = snapshot.IsComplete;
                }

                var environment = _environmentsPersistentSource.GetEnvironment(environmentName);
                if (environment is null)
                {
                    logger.LogWarning("Terraform applied-resource registration skipped: environment {EnvironmentName} was not found.", environmentName);
                    return result;
                }

                var auditUser = string.IsNullOrWhiteSpace(username) ? "DOrc deployment" : username;
                result.EnvironmentId = environment.EnvironmentId;
                foreach (var resource in resources)
                {
                    TerraformInventoryObservation? observation = null;
                    try
                    {
                        observation = new TerraformInventoryObservation
                        {
                            CloudResourceId = RegisterResource(resource, environment, auditUser),
                            ResourceIdentifier = resource.ResourceIdentifier
                        };
                        result.Observed.Add(observation);
                        result.RegisteredCount++;
                    }
                    catch (Exception ex)
                    {
                        result.FailedCount++;
                        if (result.Warnings.Count < 20) result.Warnings.Add($"Cloud registration failed for {resource.Name[..Math.Min(resource.Name.Length, 128)]}; review definition identity and request logs.");
                        logger.LogWarning(ex, "Could not register terraform-applied cloud resource {ResourceName} ({ResourceType}, {Provider}) on environment {EnvironmentName}.",
                            resource.Name, resource.ResourceType, resource.Provider, environmentName);
                    }

                    try
                    {
                        var databaseId = RegisterDatabase(resource, environment);
                        if (observation != null) observation.DatabaseId = databaseId;
                    }
                    catch (Exception ex)
                    {
                        result.FailedCount++;
                        if (result.Warnings.Count < 20) result.Warnings.Add($"Database registration failed for {resource.Name[..Math.Min(resource.Name.Length, 128)]}; review database identity and request logs.");
                        logger.LogWarning(ex, "Could not register terraform-applied database {ResourceName} ({ResourceType}) on environment {EnvironmentName}.",
                            resource.Name, resource.ResourceType, environmentName);
                    }
                }
                result.Status = result.FailedCount > 0 ? "PartialFailure" : result.SnapshotComplete ? "Succeeded" : "IncompleteExport";
            }
            catch (Exception ex)
            {
                result.Status = "Failed";
                result.FailedCount++;
                logger.LogWarning(ex, "Terraform applied-resource registration failed for environment {EnvironmentName}. The deployment itself succeeded.", environmentName);
            }
            return result;
        }

        private int RegisterResource(CloudResourceApiModel resource, EnvironmentApiModel environment, string username)
        {
            return _cloudResourcesPersistentSource.ExecuteMutation(() => RegisterResourceCore(resource, environment, username));
        }

        private int RegisterResourceCore(CloudResourceApiModel resource, EnvironmentApiModel environment, string username)
        {
            var persisted = Upsert(resource, username);
            if (persisted is null) throw new InvalidOperationException("Cloud resource identity is ambiguous.");

            var outcome = _cloudResourcesPersistentSource.AttachToEnvironment(persisted.Id, environment.EnvironmentId);
            switch (outcome)
            {
                case EnvironmentAttachmentOutcome.Attached:
                    _cloudResourceAuditPersistentSource.InsertCloudResourceAudit(
                        username,
                        ActionType.Attach,
                        persisted.Id,
                        fromValue: null,
                        toValue: $"Attached to environment '{environment.EnvironmentName}' by terraform deployment");
                    break;
                case EnvironmentAttachmentOutcome.AlreadyAttached:
                    break;
                default:
                    throw new InvalidOperationException($"Could not attach cloud resource {persisted.Id} to environment {environment.EnvironmentId}: {outcome}.");
            }
            return persisted.Id;
        }

        private CloudResourceApiModel? Upsert(CloudResourceApiModel resource, string username)
        {
            if (string.IsNullOrWhiteSpace(resource.ResourceIdentifier))
                throw new ArgumentException("Applied cloud resources must have a resource identifier.", nameof(resource));
            if (string.IsNullOrWhiteSpace(resource.Provider) || string.IsNullOrWhiteSpace(resource.ResourceType))
                throw new ArgumentException("Applied cloud resources must have a provider and resource type.", nameof(resource));

            var matches = _cloudResourcesPersistentSource.GetByIdentity(
                resource.Provider, resource.ResourceType, resource.ResourceIdentifier);
            if (matches.Count > 1)
            {
                logger.LogWarning("Ambiguous terraform cloud resource identity for {Provider}/{ResourceType}: matching definition IDs {CloudResourceIds}. Registration skipped; resolve duplicate definitions explicitly.",
                    resource.Provider, resource.ResourceType, string.Join(", ", matches.Select(match => match.Id)));
                return null;
            }
            if (matches.Count == 1)
                return UpdateIfChanged(matches[0], resource, username);

            // Display-name availability is relevant only for a new remote identity.
            foreach (var candidateName in CandidateNames(resource))
            {
                var existing = _cloudResourcesPersistentSource.GetByName(candidateName);
                if (existing is null)
                {
                    var created = _cloudResourcesPersistentSource.Add(new CloudResourceApiModel
                    {
                        Name = candidateName,
                        Provider = resource.Provider,
                        ResourceType = resource.ResourceType,
                        ResourceIdentifier = resource.ResourceIdentifier,
                        Subscription = resource.Subscription,
                        Tags = resource.Tags
                    });

                    _cloudResourceAuditPersistentSource.InsertCloudResourceAudit(
                        username,
                        ActionType.Create,
                        created.Id,
                        fromValue: null,
                        toValue: JsonSerializer.Serialize(created));

                    return created;
                }
            }

            throw new InvalidOperationException($"All identity-qualified names for cloud resource '{resource.Name}' are occupied by different resources.");
        }

        private static IEnumerable<string> CandidateNames(CloudResourceApiModel resource)
        {
            const int maxNameLength = 250;
            if (resource.Name.Length <= maxNameLength)
                yield return resource.Name;
            if (!string.IsNullOrWhiteSpace(resource.ResourceType))
            {
                var typedName = $"{resource.Name} ({resource.ResourceType})";
                if (typedName.Length <= maxNameLength)
                    yield return typedName;
            }
            var identity = CloudResourceIdentity.FingerprintInput(resource.Provider, resource.ResourceType, resource.ResourceIdentifier);
            var suffix = $" ({Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))})";
            yield return resource.Name[..Math.Min(resource.Name.Length, maxNameLength - suffix.Length)] + suffix;
        }

        /// <summary>
        /// Database-flavoured terraform resources additionally surface on the
        /// environment's Databases tab, where operators expect to find them.
        /// The terraform identifier formats are stable provider contracts:
        /// cosmos ids are ARM paths through 'databaseAccounts/{account}',
        /// Aiven ClickHouse and PostgreSQL ids are 'project/service/database'.
        /// </summary>
        private int? RegisterDatabase(CloudResourceApiModel resource, EnvironmentApiModel environment)
        {
            var database = MapDatabase(resource);
            if (database is null) return null;

            var existing = _databasesPersistentSource
                .GetDatabases(database.Name, database.ServerName)
                .FirstOrDefault();
            if (existing is not null && database.DatabaseType is { } databaseType && existing.DatabaseType != databaseType)
            {
                if (!_databasesPersistentSource.UpdateDatabaseType(existing.Id, databaseType))
                    throw new InvalidOperationException($"Database '{database.Name}' on '{database.ServerName}' disappeared during type registration.");
                existing.DatabaseType = databaseType;
            }
            var persisted = existing ?? _databasesPersistentSource.AddDatabase(database);
            if (persisted is null)
            {
                logger.LogWarning("Could not add database {DatabaseName} on {ServerName} for environment {EnvironmentName}.",
                    database.Name, database.ServerName, environment.EnvironmentName);
                throw new InvalidOperationException("The database registration could not be persisted.");
            }

            try
            {
                // The Monitor's IClaimsPrincipalReader is the static direct-tool
                // reader, so an empty principal is the supported way to say
                // "this process" for the environment history entry.
                _environmentsPersistentSource.AttachDatabaseToEnv(
                    environment.EnvironmentId, persisted.Id, new ClaimsPrincipal());
            }
            catch (ArgumentException)
            {
                // Already attached - the expected outcome on redeployments.
            }
            return persisted.Id;
        }

        private static DatabaseApiModel? MapDatabase(CloudResourceApiModel resource)
        {
            if (string.IsNullOrWhiteSpace(resource.Name)) return null;

            switch (resource.ResourceType)
            {
                case "azurerm_cosmosdb_sql_database":
                case "azurerm_cosmosdb_mongo_database":
                    var account = SegmentAfter(resource.ResourceIdentifier, "databaseAccounts");
                    if (account is null) return null;
                    return new DatabaseApiModel
                    {
                        Name = resource.Name,
                        Tags = new[] { "CosmosDB" },
                        ServerName = account,
                        DatabaseType = DatabaseType.CosmosDb,
                        AdGroup = string.Empty,
                        ArrayName = string.Empty
                    };

                case "aiven_clickhouse_database":
                case "aiven_pg_database":
                    // Service names are unique only within an Aiven project.
                    var parts = (resource.ResourceIdentifier ?? string.Empty).Split('/');
                    if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
                        throw new ArgumentException($"Expected Aiven database identifier 'project/service/database', got '{resource.ResourceIdentifier}'.");
                    return new DatabaseApiModel
                    {
                        Name = resource.Name,
                        Tags = new[] { resource.ResourceType == "aiven_pg_database" ? "PostgreSQL" : "ClickHouse" },
                        ServerName = $"{parts[0]}/{parts[1]}",
                        DatabaseType = resource.ResourceType == "aiven_pg_database"
                            ? DatabaseType.PostgreSql
                            : DatabaseType.ClickHouse,
                        AdGroup = string.Empty,
                        ArrayName = string.Empty
                    };

                default:
                    return null;
            }
        }

        private static string? SegmentAfter(string? identifier, string segment)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return null;
            var parts = identifier.Split('/');
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (string.Equals(parts[i], segment, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(parts[i + 1]))
                {
                    return parts[i + 1];
                }
            }
            return null;
        }

        private CloudResourceApiModel? UpdateIfChanged(CloudResourceApiModel existing, CloudResourceApiModel incoming, string username)
        {
            var changed = !string.Equals(existing.Provider, incoming.Provider, StringComparison.Ordinal)
                || !string.Equals(existing.ResourceType, incoming.ResourceType, StringComparison.Ordinal)
                || !string.Equals(existing.Subscription ?? string.Empty, incoming.Subscription ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!changed) return existing;

            var updated = _cloudResourcesPersistentSource.Update(existing.Id, new CloudResourceApiModel
            {
                Name = existing.Name,
                Provider = incoming.Provider,
                ResourceType = incoming.ResourceType,
                ResourceIdentifier = incoming.ResourceIdentifier,
                Subscription = incoming.Subscription,
                // Tags are operator-owned metadata; terraform runs never clobber them.
                Tags = existing.Tags
            });
            if (updated is null) return existing;

            _cloudResourceAuditPersistentSource.InsertCloudResourceAudit(
                username,
                ActionType.Update,
                existing.Id,
                fromValue: JsonSerializer.Serialize(existing),
                toValue: JsonSerializer.Serialize(updated));

            return updated;
        }
    }
}

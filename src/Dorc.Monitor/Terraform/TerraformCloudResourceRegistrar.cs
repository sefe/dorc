using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Monitor.Terraform
{
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

        public TerraformCloudResourceRegistrar(
            ILogger logger,
            ICloudResourcesPersistentSource cloudResourcesPersistentSource,
            ICloudResourceAuditPersistentSource cloudResourceAuditPersistentSource,
            IEnvironmentsPersistentSource environmentsPersistentSource)
        {
            this.logger = logger;
            _cloudResourcesPersistentSource = cloudResourcesPersistentSource;
            _cloudResourceAuditPersistentSource = cloudResourceAuditPersistentSource;
            _environmentsPersistentSource = environmentsPersistentSource;
        }

        public void RegisterAppliedResources(string appliedResourcesFilePath, string environmentName, string username)
        {
            try
            {
                var resources = ReadAppliedResources(appliedResourcesFilePath);
                if (resources.Count == 0) return;

                var environment = _environmentsPersistentSource.GetEnvironment(environmentName);
                if (environment is null)
                {
                    logger.LogWarning($"Terraform applied-resource registration skipped: environment '{environmentName}' was not found.");
                    return;
                }

                var auditUser = string.IsNullOrWhiteSpace(username) ? "DOrc deployment" : username;
                foreach (var resource in resources)
                {
                    try
                    {
                        RegisterResource(resource, environment, auditUser);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, $"Could not register terraform-applied cloud resource '{resource.Name}' ({resource.ResourceType}) on environment '{environmentName}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"Terraform applied-resource registration failed for environment '{environmentName}'. The deployment itself succeeded.");
            }
        }

        private IReadOnlyList<CloudResourceApiModel> ReadAppliedResources(string appliedResourcesFilePath)
        {
            if (string.IsNullOrWhiteSpace(appliedResourcesFilePath) || !File.Exists(appliedResourcesFilePath))
            {
                // The runner only writes the file when the apply both succeeded
                // and the state was readable; absence is a normal outcome.
                return Array.Empty<CloudResourceApiModel>();
            }

            var json = File.ReadAllText(appliedResourcesFilePath);
            return JsonSerializer.Deserialize<List<CloudResourceApiModel>>(json) ?? new List<CloudResourceApiModel>();
        }

        private void RegisterResource(CloudResourceApiModel resource, EnvironmentApiModel environment, string username)
        {
            var persisted = Upsert(resource, username);
            if (persisted is null) return;

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
                    logger.LogWarning($"Could not attach cloud resource '{persisted.Name}' (id {persisted.Id}) to environment '{environment.EnvironmentName}': {outcome}.");
                    break;
            }
        }

        private CloudResourceApiModel? Upsert(CloudResourceApiModel resource, string username)
        {
            // Names are unique across cloud resources while the terraform
            // identifier is the real identity, so a name clash with a
            // different identifier gets one disambiguation attempt before
            // being skipped.
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

                if (SameIdentity(existing, resource))
                {
                    return UpdateIfChanged(existing, resource, username);
                }
            }

            logger.LogWarning($"Skipping terraform-applied cloud resource '{resource.Name}' ({resource.ResourceType}): a different resource already uses that name.");
            return null;
        }

        private static IEnumerable<string> CandidateNames(CloudResourceApiModel resource)
        {
            yield return resource.Name;
            if (!string.IsNullOrWhiteSpace(resource.ResourceType))
            {
                yield return $"{resource.Name} ({resource.ResourceType})";
            }
        }

        private static bool SameIdentity(CloudResourceApiModel existing, CloudResourceApiModel incoming) =>
            !string.IsNullOrWhiteSpace(incoming.ResourceIdentifier)
            && string.Equals(existing.ResourceIdentifier, incoming.ResourceIdentifier, StringComparison.OrdinalIgnoreCase);

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

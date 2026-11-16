using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources
{
    public class EnvironmentTemplatesPersistentSource : IEnvironmentTemplatesPersistentSource
    {
        private static readonly HashSet<string> DisallowedDefaultTiers = new(StringComparer.OrdinalIgnoreCase)
        {
            "pr",
            "dr",
            "prd",
            "live",
            "prod",
            "production"
        };

        private readonly IDeploymentContextFactory _contextFactory;

        public EnvironmentTemplatesPersistentSource(IDeploymentContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public IEnumerable<EnvironmentTemplateApiModel> GetTemplatesForProject(int projectId)
        {
            using var context = _contextFactory.GetContext();
            var entities = context.EnvironmentTemplates
                .Include(t => t.Project)
                .Include(t => t.BaseEnvironment)
                .Where(t => t.ProjectId == projectId)
                .OrderBy(t => t.Name)
                .ToList();

            return entities.Select(e => MapToApiModel(e, context)).ToList();
        }

        public EnvironmentTemplateApiModel? GetTemplateById(int templateId)
        {
            using var context = _contextFactory.GetContext();
            var entity = context.EnvironmentTemplates
                .Include(t => t.Project)
                .Include(t => t.BaseEnvironment)
                .SingleOrDefault(t => t.Id == templateId);

            return entity == null ? null : MapToApiModel(entity, context);
        }

        public EnvironmentTemplateApiModel? GetTemplateByName(int projectId, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            using var context = _contextFactory.GetContext();
            var entity = context.EnvironmentTemplates
                .Include(t => t.Project)
                .Include(t => t.BaseEnvironment)
                .SingleOrDefault(t => t.ProjectId == projectId
                    && EF.Functions.Collate(t.Name, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(name, DeploymentContext.CaseInsensitiveCollation));

            return entity == null ? null : MapToApiModel(entity, context);
        }

        public EnvironmentTemplateApiModel CreateTemplate(EnvironmentTemplateApiModel model, string username)
        {
            ArgumentNullException.ThrowIfNull(model);

            using var context = _contextFactory.GetContext();
            ValidateTemplate(context, model, existingTemplateId: null);

            var nowUtc = DateTime.UtcNow;
            var entity = new EnvironmentTemplate
            {
                ProjectId = model.ProjectId,
                Name = model.Name.Trim(),
                Description = model.Description,
                DefaultTier = NormalizeTier(model.DefaultTier),
                BaseEnvironmentId = model.BaseEnvironmentId,
                DefaultTtlMinutes = model.DefaultTtlMinutes,
                MaxTtlMinutes = model.MaxTtlMinutes,
                ProvisionComponentIdsJson = SerializeComponentIds(model.ProvisionComponentIds),
                ValidationComponentIdsJson = SerializeComponentIds(model.ValidationComponentIds),
                TierParameterOverridesJson = model.TierParameterOverridesJson,
                CreatedBy = !string.IsNullOrWhiteSpace(username) ? username : model.CreatedBy,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };

            context.EnvironmentTemplates.Add(entity);
            context.SaveChanges();

            return MapToApiModel(entity, context);
        }

        public EnvironmentTemplateApiModel UpdateTemplate(EnvironmentTemplateApiModel model, string username)
        {
            ArgumentNullException.ThrowIfNull(model);

            using var context = _contextFactory.GetContext();
            var entity = context.EnvironmentTemplates
                .Include(t => t.Project)
                .Include(t => t.BaseEnvironment)
                .SingleOrDefault(t => t.Id == model.Id)
                ?? throw new ArgumentException($"Environment template with ID {model.Id} was not found.", nameof(model));

            if (model.ProjectId <= 0)
            {
                model.ProjectId = entity.ProjectId;
            }

            ValidateTemplate(context, model, existingTemplateId: entity.Id);

            entity.ProjectId = model.ProjectId;
            entity.Name = model.Name.Trim();
            entity.Description = model.Description;
            entity.DefaultTier = NormalizeTier(model.DefaultTier);
            entity.BaseEnvironmentId = model.BaseEnvironmentId;
            entity.DefaultTtlMinutes = model.DefaultTtlMinutes;
            entity.MaxTtlMinutes = model.MaxTtlMinutes;
            entity.ProvisionComponentIdsJson = SerializeComponentIds(model.ProvisionComponentIds);
            entity.ValidationComponentIdsJson = SerializeComponentIds(model.ValidationComponentIds);
            entity.TierParameterOverridesJson = model.TierParameterOverridesJson;
            entity.UpdatedAtUtc = DateTime.UtcNow;

            context.SaveChanges();

            return MapToApiModel(entity, context);
        }

        internal static readonly object TemplateReferenceSyncRoot = new();

        internal static void PrepareProjectTemplatesForDeletion(IDeploymentContext context, int projectId, string projectName)
        {
            if (context.Environments != null && context.TerraformDeploymentReceipts != null)
            {
                var activeEphemeralEnvs = context.Environments
                    .Where(e => e.IsEphemeral && !string.Equals(e.EphemeralState, EphemeralEnvironmentStates.TornDown, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (activeEphemeralEnvs.Count > 0)
                {
                    var activeById = activeEphemeralEnvs.ToDictionary(e => e.Id, e => e.Name);
                    var activeByName = activeEphemeralEnvs.ToDictionary(e => e.Name, e => e.Name, StringComparer.OrdinalIgnoreCase);
                    var activeIds = activeById.Keys.ToHashSet();
                    var activeNames = activeEphemeralEnvs.Select(e => e.Name).ToList();
                    var candidateReceipts = context.TerraformDeploymentReceipts
                        .Where(r => r.Operation != "Destroy" &&
                                    ((r.EnvironmentId.HasValue && activeIds.Contains(r.EnvironmentId.Value)) ||
                                     (!r.EnvironmentId.HasValue && activeNames.Contains(r.EnvironmentName))))
                        .ToList();

                    var activeReceiptEnvNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var receipt in candidateReceipts)
                    {
                        var matchesProject = receipt.ProjectId == projectId ||
                            (!string.IsNullOrWhiteSpace(projectName) &&
                             string.Equals(receipt.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
                        if (!matchesProject)
                            continue;

                        var envName = receipt.EnvironmentId.HasValue
                            ? activeById[receipt.EnvironmentId.Value]
                            : activeByName[receipt.EnvironmentName];
                        activeReceiptEnvNames.Add(envName);
                    }

                    if (activeReceiptEnvNames.Count > 0)
                    {
                        var envNames = string.Join(", ", activeReceiptEnvNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                        throw new InvalidOperationException(
                            $"The project \"{projectName}\" cannot be deleted while its Terraform resources exist in active ephemeral environment(s): {envNames}. Tear down those environments before trying again.");
                    }
                }
            }

            if (context.EnvironmentTemplates == null)
                return;

            var templates = context.EnvironmentTemplates.Where(t => t.ProjectId == projectId).ToList();
            if (templates.Count == 0)
                return;

            if (context.Environments != null)
            {
                var templateIds = templates.Select(t => t.Id).ToHashSet();
                var referencingEnvs = context.Environments
                    .Where(e => e.TemplateId.HasValue && templateIds.Contains(e.TemplateId.Value))
                    .ToList();
                var activeEphemeralEnvs = referencingEnvs
                    .Where(e => e.IsEphemeral && !string.Equals(e.EphemeralState, EphemeralEnvironmentStates.TornDown, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (activeEphemeralEnvs.Count > 0)
                {
                    var envNames = string.Join(", ", activeEphemeralEnvs.Select(e => e.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                    throw new InvalidOperationException(
                        $"The project \"{projectName}\" cannot be deleted while its environment templates are referenced by active ephemeral environment(s): {envNames}. Tear down those environments before trying again.");
                }

                foreach (var env in referencingEnvs)
                {
                    env.TemplateId = null;
                }
            }

            context.EnvironmentTemplates.RemoveRange(templates);
        }

        public bool DeleteTemplate(int templateId)
        {
            lock (TemplateReferenceSyncRoot)
            {
                using var context = _contextFactory.GetContext();
                var entity = context.EnvironmentTemplates.SingleOrDefault(t => t.Id == templateId);
                if (entity == null)
                    return false;

                if (context.Environments != null)
                {
                    var referencingEnvs = context.Environments.Where(e => e.TemplateId == templateId).ToList();
                    var activeEphemeralEnvs = referencingEnvs
                        .Where(e => e.IsEphemeral && !string.Equals(e.EphemeralState, EphemeralEnvironmentStates.TornDown, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (activeEphemeralEnvs.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"Cannot delete environment template '{entity.Name}' while it is referenced by active ephemeral environment(s): {string.Join(", ", activeEphemeralEnvs.Select(e => e.Name))}.");
                    }

                    foreach (var env in referencingEnvs)
                    {
                        env.TemplateId = null;
                    }
                }

                context.EnvironmentTemplates.Remove(entity);
                context.SaveChanges();
                return true;
            }
        }

        private static void ValidateTemplate(IDeploymentContext context, EnvironmentTemplateApiModel model, int? existingTemplateId)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                throw new ArgumentException("Template Name is required.", nameof(model));
            }

            if (model.ProjectId <= 0)
            {
                throw new ArgumentException("A valid ProjectId is required.", nameof(model));
            }

            if (!context.Projects.Any(p => p.Id == model.ProjectId))
            {
                throw new ArgumentException($"Project with ID {model.ProjectId} was not found.", nameof(model));
            }

            if (model.DefaultTtlMinutes <= 0)
            {
                throw new ArgumentException("DefaultTtlMinutes must be greater than 0.", nameof(model));
            }

            if (model.MaxTtlMinutes < model.DefaultTtlMinutes)
            {
                throw new ArgumentException("MaxTtlMinutes must be greater than or equal to DefaultTtlMinutes.", nameof(model));
            }

            if (model.MaxTtlMinutes > 10080)
            {
                throw new ArgumentException("MaxTtlMinutes cannot exceed 10080 minutes (7 days).", nameof(model));
            }

            if (!string.IsNullOrWhiteSpace(model.TierParameterOverridesJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(model.TierParameterOverridesJson);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        throw new ArgumentException("TierParameterOverridesJson must be a valid JSON object.", nameof(model));
                    }
                }
                catch (JsonException ex)
                {
                    throw new ArgumentException($"TierParameterOverridesJson is not valid JSON: {ex.Message}", nameof(model), ex);
                }
            }

            var tier = NormalizeTier(model.DefaultTier);
            if (DisallowedDefaultTiers.Contains(tier))
            {
                throw new ArgumentException($"DefaultTier cannot be a production tier ('{model.DefaultTier}').", nameof(model));
            }

            if (model.BaseEnvironmentId.HasValue)
            {
                var baseEnv = context.Environments.SingleOrDefault(e => e.Id == model.BaseEnvironmentId.Value)
                    ?? throw new ArgumentException($"Base environment with ID {model.BaseEnvironmentId.Value} was not found.", nameof(model));

                if (baseEnv.IsProd)
                {
                    throw new ArgumentException("BaseEnvironmentId cannot reference a production environment (IsProd = true).", nameof(model));
                }
            }

            var trimmedName = model.Name.Trim();
            var duplicateExists = context.EnvironmentTemplates.Any(t =>
                t.ProjectId == model.ProjectId
                && (!existingTemplateId.HasValue || t.Id != existingTemplateId.Value)
                && EF.Functions.Collate(t.Name, DeploymentContext.CaseInsensitiveCollation)
                    == EF.Functions.Collate(trimmedName, DeploymentContext.CaseInsensitiveCollation));

            if (duplicateExists)
            {
                throw new ArgumentException($"An environment template named '{trimmedName}' already exists in project {model.ProjectId}.", nameof(model));
            }
        }

        private static string NormalizeTier(string? tier)
        {
            return string.IsNullOrWhiteSpace(tier) ? "ephemeral" : tier.Trim().ToLowerInvariant();
        }

        private static string SerializeComponentIds(IList<int>? componentIds)
        {
            if (componentIds == null || componentIds.Count == 0)
                return "[]";

            return JsonSerializer.Serialize(componentIds);
        }

        private static IList<int> DeserializeComponentIds(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<int>();

            try
            {
                return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
            }
            catch
            {
                return new List<int>();
            }
        }

        private static EnvironmentTemplateApiModel MapToApiModel(EnvironmentTemplate entity, IDeploymentContext context)
        {
            var projectName = entity.Project != null
                ? entity.Project.Name
                : context.Projects.FirstOrDefault(p => p.Id == entity.ProjectId)!.Name;

            var baseEnvName = entity.BaseEnvironment != null
                ? entity.BaseEnvironment.Name
                : (entity.BaseEnvironmentId.HasValue
                    ? context.Environments.FirstOrDefault(e => e.Id == entity.BaseEnvironmentId.Value)!.Name
                    : null);

            return new EnvironmentTemplateApiModel
            {
                Id = entity.Id,
                ProjectId = entity.ProjectId,
                ProjectName = projectName!,
                Name = entity.Name,
                Description = entity.Description!,
                DefaultTier = entity.DefaultTier,
                BaseEnvironmentId = entity.BaseEnvironmentId,
                BaseEnvironmentName = baseEnvName!,
                DefaultTtlMinutes = entity.DefaultTtlMinutes,
                MaxTtlMinutes = entity.MaxTtlMinutes,
                ProvisionComponentIds = DeserializeComponentIds(entity.ProvisionComponentIdsJson),
                ValidationComponentIds = DeserializeComponentIds(entity.ValidationComponentIdsJson),
                TierParameterOverridesJson = entity.TierParameterOverridesJson!,
                CreatedBy = entity.CreatedBy!,
                CreatedAtUtc = entity.CreatedAtUtc,
                UpdatedAtUtc = entity.UpdatedAtUtc
            };
        }
    }
}

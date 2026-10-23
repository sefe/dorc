using System.Data;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public partial class RequestsPersistentSource
{
    public CatalogEnvironmentInputs? GetCatalogEnvironmentInputs(int componentId, int environmentId)
    {
        using var context = _contextFactory.GetContext();
        return context.CatalogEnvironmentInputs.AsNoTracking()
            .SingleOrDefault(row => row.ComponentId == componentId && row.EnvironmentId == environmentId);
    }

    public CatalogRequestInputs? GetCatalogRequestInputs(int requestId, int componentId)
    {
        using var context = _contextFactory.GetContext();
        return context.CatalogRequestInputs.AsNoTracking()
            .SingleOrDefault(row => row.ComponentId == componentId && row.DeploymentRequestId == requestId);
    }

    public long SaveCatalogEnvironmentInputs(int componentId, int projectId, int environmentId, string templateVersion,
        long expectedRevision, IDictionary<string, string> inputs, string actor = "")
    {
        using var strategyContext = _contextFactory.GetContext();
        return strategyContext.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var context = _contextFactory.GetContext();
            using var transaction = context.Database.BeginTransaction(IsolationLevel.Serializable);
            var before = DeploymentDefinitions.Read(context, projectId);
            var component = context.Components
                .FromSqlRaw("SELECT * FROM [deploy].[Component] WITH (UPDLOCK,HOLDLOCK)")
                .SingleOrDefault(c => c.Id == componentId && c.Projects.Any(p => p.Id == projectId));
            if (component?.TerraformSourceType != TerraformSourceType.Catalog ||
                component.TerraformTemplateVersion != templateVersion ||
                !context.Environments.Any(e => e.Id == environmentId && e.Projects.Any(p => p.Id == projectId)))
                throw new InvalidOperationException("The component pin or project/environment mapping changed. Reload before saving.");
            RejectSecureCatalogInputs(context, inputs.Keys);
            var current = context.CatalogEnvironmentInputs
                .SingleOrDefault(row => row.ComponentId == componentId && row.EnvironmentId == environmentId);
            if ((current?.Revision ?? 0) != expectedRevision)
                throw new InvalidOperationException("Saved inputs changed. Reload before saving.");
            if (current == null)
            {
                current = new CatalogEnvironmentInputs { ComponentId = componentId, EnvironmentId = environmentId };
                context.CatalogEnvironmentInputs.Add(current);
            }
            current.InputsJson = JsonSerializer.Serialize(inputs);
            current.TemplateVersion = templateVersion;
            current.Revision = checked(expectedRevision + 1);
            context.SaveChanges();
            DeploymentDefinitions.Record(context, before, DeploymentDefinitions.Read(context, projectId), actor, "Catalog.InputsSaved",
                $"CatalogEnvironmentInputs:{componentId}:{environmentId}:{current.Revision}");
            context.SaveChanges();
            transaction.Commit();
            return current.Revision;
        });
    }

    private static void RejectSecureCatalogInputs(IDeploymentContext context, IEnumerable<string> names)
    {
        var secureNames = context.Properties.Where(property => property.Secure).Select(property => property.Name)
            .Concat(context.ConfigValues.Where(value => value.Secure).Select(value => value.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Any(name => secureNames.Contains(name) ||
            name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_key", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Sensitive inputs cannot be saved here. Use secure DOrc properties or one-off request overrides.");
    }

    private static List<CatalogRequestInputs> CaptureCatalogInputs(IDeploymentContext context, DeploymentRequest request)
    {
        var names = (request.Components ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);
        var components = context.Components
            .FromSqlRaw("SELECT * FROM [deploy].[Component] WITH (UPDLOCK,HOLDLOCK)")
            .Where(component => names.Contains(component.Name) && component.TerraformSourceType == TerraformSourceType.Catalog)
            .ToList();
        var environmentId = context.Environments.Where(environment => environment.Name == request.Environment)
            .Select(environment => (int?)environment.Id).SingleOrDefault();
        var snapshots = new List<CatalogRequestInputs>();
        foreach (var component in components)
        {
            var configuration = environmentId.HasValue ? context.CatalogEnvironmentInputs.SingleOrDefault(row =>
                row.ComponentId == component.Id && row.EnvironmentId == environmentId) : null;
            if (configuration != null && configuration.TemplateVersion != component.TerraformTemplateVersion)
                throw new InvalidOperationException("Saved component inputs target a different module version. Review and save compatible inputs first.");
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(configuration?.InputsJson ?? "{}")!;
            RejectSecureCatalogInputs(context, values.Keys);
            snapshots.Add(new CatalogRequestInputs
            {
                ComponentId = component.Id,
                TemplateName = component.TerraformTemplateName!,
                TemplateVersion = component.TerraformTemplateVersion!,
                ConfigurationRevision = configuration?.Revision ?? 0,
                InputsJson = JsonSerializer.Serialize(values)
            });
        }
        return snapshots;
    }
}

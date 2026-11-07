using System.Data;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public partial class RequestsPersistentSource
{
    public CatalogUpgradePreviewApiModel GetCatalogUpgradeScope(int componentId, int projectId)
    {
        using var context = _contextFactory.GetContext();
        var component = context.Components.Include(c => c.Projects).Single(c => c.Id == componentId);
        if (component.Projects.Count != 1 || component.Projects.Single().Id != projectId ||
            component.TerraformSourceType != TerraformSourceType.Catalog)
            throw new InvalidOperationException("Upgrades require a Catalog component owned by exactly this project.");
        return new CatalogUpgradePreviewApiModel
        {
            ComponentId = componentId,
            CurrentVersion = component.TerraformTemplateVersion!,
            Revision = context.CatalogModuleUpgrades.Where(row => row.ComponentId == componentId)
                .Select(row => (long?)row.Revision).Max() ?? 0,
            Environments = context.Environments.Where(environment => environment.Projects.Any(project => project.Id == projectId))
                .Select(environment => new EnvironmentApiModel { EnvironmentId = environment.Id, EnvironmentName = environment.Name,
                    EnvironmentIsProd = environment.IsProd }).ToList()
        };
    }

    public void UpgradeCatalogComponent(int componentId, int projectId, string targetVersion,
        CatalogUpgradeRequestApiModel expected, IDictionary<int, Dictionary<string, string>> inputs, string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        using var strategyContext = _contextFactory.GetContext();
        strategyContext.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var context = _contextFactory.GetContext();
            using var transaction = context.Database.BeginTransaction(IsolationLevel.Serializable);
            var before = DeploymentDefinitions.Read(context, projectId);
            var component = context.Components.FromSqlRaw("SELECT * FROM [deploy].[Component] WITH (UPDLOCK,HOLDLOCK)")
                .Include(c => c.Projects).Single(c => c.Id == componentId);
            var revision = context.CatalogModuleUpgrades.Where(row => row.ComponentId == componentId)
                .Select(row => (long?)row.Revision).Max() ?? 0;
            if (!expected.ConfirmImpact || revision != expected.ExpectedRevision ||
                component.TerraformTemplateVersion != expected.ExpectedVersion ||
                component.TerraformSourceType != TerraformSourceType.Catalog ||
                component.Projects.Count != 1 || component.Projects.Single().Id != projectId)
                throw new InvalidOperationException("The component changed. Reload the upgrade preview.");
            var environments = context.Environments.Where(e => e.Projects.Any(p => p.Id == projectId)).ToList();
            if (!environments.Select(e => e.Id).Order().SequenceEqual(expected.EnvironmentIds.Order()) ||
                !environments.Select(e => e.Id).Order().SequenceEqual(inputs.Keys.Order()))
                throw new InvalidOperationException("Mapped environments changed. Reload the upgrade impact.");
            var environmentNames = environments.Select(e => e.Name).ToArray();
            var projectName = component.Projects.Single().Name;
            var terminal = new[] { "Completed", "Cancelled", "Errored", "Failed", "Abandoned" };
            if (context.DeploymentRequests.Any(request => request.Project == projectName &&
                environmentNames.Contains(request.Environment!) && !terminal.Contains(request.Status)))
                throw new InvalidOperationException("Affected project environments have active, pending or approval-waiting requests. Finish or cancel them before upgrading.");
            foreach (var environment in environments)
            {
                var configuration = context.CatalogEnvironmentInputs.SingleOrDefault(row =>
                    row.ComponentId == componentId && row.EnvironmentId == environment.Id);
                if (!expected.ConfigurationRevisions.TryGetValue(environment.Id.ToString(), out var expectedConfiguration) ||
                    expectedConfiguration != (configuration?.Revision ?? 0))
                    throw new InvalidOperationException("Saved inputs changed. Reload the upgrade preview.");
                RejectSecureCatalogInputs(context, inputs[environment.Id].Keys);
                if (configuration == null && inputs[environment.Id].Count == 0) continue;
                if (configuration == null)
                {
                    configuration = new CatalogEnvironmentInputs { ComponentId = componentId, EnvironmentId = environment.Id };
                    context.CatalogEnvironmentInputs.Add(configuration);
                }
                configuration.InputsJson = JsonSerializer.Serialize(inputs[environment.Id]);
                configuration.TemplateVersion = targetVersion;
                configuration.Revision++;
            }
            context.CatalogModuleUpgrades.Add(new CatalogModuleUpgrade
            {
                ComponentId = componentId, Revision = revision + 1,
                FromVersion = component.TerraformTemplateVersion!, ToVersion = targetVersion,
                UpdatedBy = username, UpdatedAt = DateTimeOffset.UtcNow
            });
            component.TerraformTemplateVersion = targetVersion;
            context.SaveChanges();
            DeploymentDefinitions.Record(context, before, DeploymentDefinitions.Read(context, projectId), username, "Catalog.Upgrade",
                $"CatalogModuleUpgrade:{componentId}:{revision + 1}");
            context.SaveChanges();
            transaction.Commit();
        });
    }
}

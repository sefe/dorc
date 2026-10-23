using System.Data;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

public partial class ManageProjectsPersistentSource
{
    /// <summary>
    /// Atomically creates a Catalog component or, for deployment retries, reuses
    /// an enabled component with the same project, template, version and parent.
    /// Unlike legacy reference-data creation, this never renames or edits a collision.
    /// </summary>
    public CatalogComponentCreation CreateCatalogComponent(
        ComponentApiModel component, int projectId, int? parentId, bool allowReuse, string actor = "")
    {
        ArgumentNullException.ThrowIfNull(component);
        ValidateAllComponentIdsAreZero(component, HttpRequestType.Post);
        ValidateComponentNameAndIdAreNotEmpty(component);
        ValidateComponentNameCharacters(component);
        ValidateNameLengthRestrictions(component);
        if (component.ComponentType != ComponentType.Terraform
            || component.TerraformSourceType != TerraformSourceType.Catalog
            || string.IsNullOrWhiteSpace(component.TerraformTemplateName)
            || component.TerraformTemplateName.Length > 256
            || string.IsNullOrWhiteSpace(component.TerraformTemplateVersion)
            || component.TerraformTemplateVersion.Length > 64)
            throw new ArgumentException("A valid Catalog template name and version are required.", nameof(component));

        using var strategyContext = _contextFactory.GetContext();
        return strategyContext.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var context = _contextFactory.GetContext();
            using var transaction = context.Database.BeginTransaction(IsolationLevel.Serializable);

            // Lock the global name range before checking ownership or inserting.
            // UPDLOCK prevents competing creators from both holding a shared gap
            // lock and then deadlocking on insert. HOLDLOCK also protects absence.
            var collisions = context.Components
                .FromSqlRaw("SELECT * FROM [deploy].[Component] WITH (UPDLOCK, HOLDLOCK)")
                .Where(c => EF.Functions.Collate(c.Name, DeploymentContext.CaseInsensitiveCollation)
                    == EF.Functions.Collate(component.ComponentName, DeploymentContext.CaseInsensitiveCollation))
                .Include(c => c.Projects)
                .Include(c => c.Parent)
                .ToList();

            var project = context.Projects.SingleOrDefault(p => p.Id == projectId);
            if (project is null)
                return new(CatalogComponentCreationStatus.ProjectNotFound,
                    Message: $"Project with id {projectId} was not found.");

            if (collisions.Count > 0)
            {
                var existing = collisions[0];
                if (collisions.Count != 1
                    || existing.Projects.Count != 1
                    || existing.Projects.Single().Id != projectId
                    || !allowReuse
                    || existing.ComponentType != ComponentType.Terraform
                    || existing.TerraformSourceType != TerraformSourceType.Catalog
                    || !string.Equals(existing.TerraformTemplateName, component.TerraformTemplateName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing.TerraformTemplateVersion, component.TerraformTemplateVersion, StringComparison.OrdinalIgnoreCase)
                    || existing.Parent?.Id != parentId)
                    return new(CatalogComponentCreationStatus.Conflict,
                        Message: $"A component named '{component.ComponentName}' already exists with a different identity or cannot be reused. Choose a different component name or retry with the original project, template version and parent.");

                if (!existing.IsEnabled)
                    return new(CatalogComponentCreationStatus.Disabled,
                        Message: $"Component '{existing.Name}' is disabled. Enable it before retrying the deployment.");
            }

            var parent = parentId.HasValue
                ? context.Components.SingleOrDefault(c => c.Id == parentId && c.Projects.Any(p => p.Id == projectId))
                : null;
            if (parentId.HasValue && parent is null)
                return new(CatalogComponentCreationStatus.InvalidParent,
                    Message: $"Parent component id {parentId} does not belong to project '{project.Name}'.");

            if (collisions.Count == 1)
            {
                var result = new CatalogComponentCreation(CatalogComponentCreationStatus.Reused,
                    MapToComponentApiModel(collisions[0]));
                transaction.Commit();
                return result;
            }

            var created = new Component
            {
                Name = component.ComponentName,
                Parent = parent,
                ObjectId = Guid.NewGuid(),
                Description = "Created via API",
                IsEnabled = true,
                StopOnFailure = component.StopOnFailure,
                ComponentType = ComponentType.Terraform,
                TerraformSourceType = TerraformSourceType.Catalog,
                TerraformTemplateName = component.TerraformTemplateName,
                TerraformTemplateVersion = component.TerraformTemplateVersion,
                Projects = new List<Project> { project }
            };
            var before = DeploymentDefinitions.Read(context, projectId);
            context.Components.Add(created);
            context.SaveChanges();
            DeploymentDefinitions.Record(context, before, DeploymentDefinitions.Read(context, projectId), actor, "Catalog.Create", $"Component:{created.Id}");
            context.SaveChanges();
            transaction.Commit();
            return new(CatalogComponentCreationStatus.Created, MapToComponentApiModel(created));
        });
    }
}

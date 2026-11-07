using Dorc.ApiModel;
using Dorc.Core.Interfaces;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Dorc.Api.Controllers;

[Authorize]
[ApiController]
[Route("DeploymentDefinitions")]
public sealed class DeploymentDefinitionsController(IDeploymentContextFactory factory,
    DeploymentDefinitions definitions, ISecurityPrivilegesChecker security,
    IProjectsPersistentSource projects) : ControllerBase
{
    private (int[] Projects, string[] Environments, HashSet<int> CanDeployProjects, HashSet<int> CanModifyProjects) Scope(int? projectId)
    {
        var visible = new List<int>();
        var environments = new HashSet<string>();
        var canDeploy = new HashSet<int>();
        var canModify = new HashSet<int>();
        foreach (var project in projects.GetProjects(User).Where(p => !projectId.HasValue || p.ProjectId == projectId))
        {
            var accessible = projects.GetProjectEnvironments(project.ProjectName, User, true)?.Items?.ToList() ?? [];
            var modifiable = security.CanModifyProject(User, project.ProjectId);
            if (modifiable) canModify.Add(project.ProjectId);
            if (modifiable || accessible.Count > 0) visible.Add(project.ProjectId);
            if (accessible.Any(e => e.UserEditable) ||
                projects.GetProjectEnvironments(project.ProjectName, User, false)?.Items?.Any() == true)
                canDeploy.Add(project.ProjectId);
            foreach (var environment in accessible) environments.Add(environment.EnvironmentName);
        }
        return (visible.ToArray(), environments.ToArray(), canDeploy, canModify);
    }

    [HttpGet]
    [SwaggerResponse(200, Type = typeof(GetDeploymentDefinitionsResponse))]
    public IActionResult Get(int? projectId = null, ComponentType? componentType = null,
        TerraformSourceType? sourceType = null, string? search = null, int page = 1, int limit = 50)
    {
        if (page < 1 || limit < 1 || limit > 200) return BadRequest("Page must be positive and limit between 1 and 200.");
        var scope = Scope(projectId);
        using var context = factory.GetContext();
        var query = context.Projects.Where(p => scope.Projects.Contains(p.Id))
            .SelectMany(p => p.Components.Select(c => new { Project = p, Component = c }));
        if (componentType.HasValue) query = query.Where(x => x.Component.ComponentType == componentType);
        if (sourceType.HasValue) query = query.Where(x => x.Component.ComponentType == ComponentType.Terraform &&
            x.Component.TerraformSourceType == sourceType);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Component.Name.Contains(search) || x.Project.Name.Contains(search));
        var total = query.Count();
        var rows = query.OrderBy(x => x.Project.Name).ThenBy(x => x.Component.Name)
            .ThenBy(x => x.Project.Id).ThenBy(x => x.Component.Id)
            .Skip(checked((page - 1) * limit)).Take(limit)
            .Select(x => new { ProjectId = x.Project.Id, ComponentId = x.Component.Id, x.Component.ScriptId,
                SharedComponent = x.Component.Projects.Count > 1 }).ToList();
        var documents = definitions.GetMany(rows.Select(x => x.ProjectId).Distinct().ToArray());
        var items = rows.Select(row =>
        {
            var document = documents[row.ProjectId];
            var component = Flatten(document.Components).Single(c => c.ComponentId == row.ComponentId);
            return new DeploymentDefinitionApiModel
            {
                Component = component, ProjectId = row.ProjectId, ProjectName = document.Project.ProjectName,
                Revision = document.Revision, ScriptId = row.ScriptId,
                CanEdit = !row.SharedComponent && scope.CanModifyProjects.Contains(row.ProjectId),
                CanDeploy = scope.CanDeployProjects.Contains(row.ProjectId),
                SourceSummary = component.ComponentType == ComponentType.PowerShell ? "PowerShell " + component.PSVersion :
                    component.TerraformSourceType == TerraformSourceType.Catalog ?
                        $"Catalog {component.TerraformTemplateName}@{component.TerraformTemplateVersion}" :
                        "Terraform " + component.TerraformSourceType
            };
        }).ToList();
        return Ok(new GetDeploymentDefinitionsResponse { Items = items, TotalItems = total, CurrentPage = page });
    }

    [HttpGet("history")]
    [SwaggerResponse(200, Type = typeof(GetDeploymentDefinitionHistoryResponse))]
    public IActionResult History(int? projectId = null, int? componentId = null, int page = 1, int limit = 50)
    {
        if (page < 1 || limit < 1 || limit > 200) return BadRequest("Page must be positive and limit between 1 and 200.");
        var scope = Scope(projectId);
        using var context = factory.GetContext();
        var archivedRequests = context.Set<Dorc.PersistentData.Contexts.ArchivedDeploymentRequest>().AsNoTracking();
        var query = context.Set<DeploymentDefinitionHistoryApiModel>().AsNoTracking()
            .Where(x => scope.Projects.Contains(x.ProjectId) && (!x.RequestId.HasValue ||
                (scope.Environments.Length > 0 && (context.DeploymentRequests.Any(r => r.Id == x.RequestId && scope.Environments.Contains(r.Environment!)) ||
                    archivedRequests.Any(r => r.Id == x.RequestId && scope.Environments.Contains(r.Environment!))))));
        if (componentId.HasValue) query = query.Where(x => x.ComponentId == componentId);
        var total = query.Count();
        var rows = query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip(checked((page - 1) * limit)).Take(limit).ToList();
        return Ok(new GetDeploymentDefinitionHistoryResponse { Items = rows, TotalItems = total, CurrentPage = page });
    }

    private static IEnumerable<ComponentApiModel> Flatten(IEnumerable<ComponentApiModel> components)
    {
        foreach (var component in components)
        {
            yield return component;
            foreach (var child in Flatten(component.Children ?? [])) yield return child;
        }
    }
}

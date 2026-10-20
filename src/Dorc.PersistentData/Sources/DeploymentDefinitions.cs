using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Microsoft.EntityFrameworkCore;

namespace Dorc.PersistentData.Sources;

/// <summary>Owns definition writes and their evidence on one context and transaction.</summary>
public sealed partial class DeploymentDefinitions(IDeploymentContextFactory factory) : Interfaces.IDeploymentDefinitions
{
    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static void RequireRevision(string? expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected)) throw new DefinitionPreconditionException(true);
        if (!string.Equals(expected, actual, StringComparison.Ordinal)) throw new DefinitionPreconditionException(false);
    }

    public RefDataApiModel Get(int projectId)
    {
        using var context = factory.GetContext();
        return Read(context, projectId);
    }

    public void DeleteProject(int projectId, string expectedRevision, string actor, Func<int, bool> canModify)
    {
        if (string.IsNullOrWhiteSpace(expectedRevision)) throw new DefinitionPreconditionException(true);
        using var strategyContext = factory.GetContext();
        strategyContext.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var context = factory.GetContext();
            using var transaction = context.Database.BeginTransaction(IsolationLevel.Serializable);
            var project = context.Projects.FromSqlRaw("SELECT * FROM [deploy].[Project] WITH (UPDLOCK,HOLDLOCK)")
                .Include(p => p.Components).Include(p => p.Environments).Single(p => p.Id == projectId);
            if (!canModify(projectId)) throw new UnauthorizedAccessException();
            var before = Read(context, projectId);
            RequireRevision(expectedRevision, before.Revision);
            Record(context, before, new RefDataApiModel
            { Project = before.Project, Components = [], Revision = Hash(before.Revision + ":deleted") }, actor, "Project.Delete");
            project.Components.Clear();
            project.Environments.Clear();
            context.Projects.Remove(project);
            context.SaveChanges();
            transaction.Commit();
        });
    }

    internal static string ScriptRevision(Script script) => Hash(JsonSerializer.Serialize(new
    {
        script.Id, script.Name, script.Path, script.IsPathJSON, script.NonProdOnly, script.PowerShellVersionNumber,
        Consumers = script.Components.OrderBy(c => c.Id).Select(c => new
        { c.Id, c.IsEnabled, Projects = c.Projects.Select(p => p.Id).Order().ToArray() })
    }));

    public bool EditScript(ScriptApiModel input, string actor, Func<int, bool> canModify)
    {
        if (string.IsNullOrWhiteSpace(input.Revision)) throw new DefinitionPreconditionException(true);
        ValidateNonSecret(input.Path);
        var confinement = Security.ScriptPathConfinement.Check(input.Path);
        if (!confinement.Allowed) throw new ArgumentException("The script path is outside the permitted root.");
        using var strategyContext = factory.GetContext();
        return strategyContext.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var context = factory.GetContext();
            using var transaction = context.Database.BeginTransaction(IsolationLevel.Serializable);
            var script = context.Scripts.FromSqlRaw("SELECT * FROM [deploy].[Script] WITH (UPDLOCK,HOLDLOCK)")
                .Include(s => s.Components).ThenInclude(c => c.Projects).Single(s => s.Id == input.Id);
            ValidateKnownSensitiveArguments(context, input.Path);
            RequireRevision(input.Revision, ScriptRevision(script));
            var projects = script.Components.SelectMany(c => c.Projects).Select(p => p.Id).Distinct().Order().ToArray();
            if (projects.Any(id => !canModify(id))) throw new UnauthorizedAccessException();
            if (!input.ConfirmImpact || !(input.AffectedProjectIds ?? []).Order().SequenceEqual(projects))
                throw new ArgumentException("Confirm the complete current list of affected projects before editing shared script content.");
            var before = projects.ToDictionary(id => id, id => Read(context, id));
            script.Name = input.Name;
            script.Path = input.Path;
            script.IsPathJSON = IsJson(input.Path);
            script.NonProdOnly = input.NonProdOnly;
            script.PowerShellVersionNumber = input.PowerShellVersionNumber.ToSafePsVersionString();
            foreach (var component in script.Components) component.IsEnabled = input.IsEnabled;
            context.SaveChanges();
            foreach (var id in projects) Record(context, before[id], Read(context, id), actor, "Script.SharedEdit");
            context.AuditScripts.Add(new AuditScript
            {
                ScriptId = script.Id, ScriptName = script.Name, UpdatedBy = actor, UpdatedDate = DateTime.UtcNow,
                Type = "Update", FromValue = "See deployment definition changes", ToValue = "See deployment definition changes",
                ProjectNames = string.Join(", ", script.Components.SelectMany(c => c.Projects).Select(p => p.Name).Distinct())
            });
            context.SaveChanges();
            transaction.Commit();
            return true;
        });
    }

    internal static RefDataApiModel Read(IDeploymentContext context, int projectId)
    {
        var project = context.Projects.Include(p => p.SourceDatabase).Single(p => p.Id == projectId);
        var components = context.Components.Include(c => c.Script).Include(c => c.Parent)
            .Include(c => c.Projects).Where(c => c.Projects.Any(p => p.Id == projectId))
            .OrderBy(c => c.Name).ThenBy(c => c.Id).ToList();
        var models = components.ToDictionary(c => c.Id, Map);
        foreach (var component in components)
            if (component.Parent != null && models.TryGetValue(component.Parent.Id, out var parent))
                parent.Children.Add(models[component.Id]);
        var result = new RefDataApiModel
        {
            Project = ProjectsPersistentSource.MapToProjectApiModel(project)!,
            Components = components.Where(c => c.Parent == null || !models.ContainsKey(c.Parent.Id))
                .Select(c => models[c.Id]).ToList()
        };
        // Membership and saved inputs are part of the concurrency boundary, even when
        // they are not editable through the reference-data document.
        result.Revision = Hash(JsonSerializer.Serialize(new
        {
            result.Project, result.Components,
            Membership = components.Select(c => new { c.Id, c.ScriptId, Projects = c.Projects.Select(p => p.Id).Order().ToArray() }),
            Inputs = context.CatalogEnvironmentInputs.Where(x => components.Select(c => c.Id).Contains(x.ComponentId))
                .OrderBy(x => x.ComponentId).ThenBy(x => x.EnvironmentId)
                .Select(x => new { x.ComponentId, x.EnvironmentId, x.Revision }).ToList()
        }));
        result.Project.Revision = result.Revision;
        return result;
    }

    public RefDataApiModel Save(RefDataApiModel input, bool create, string actor,
        Func<int, bool> canModify, Action validate, bool projectOnly = false)
        => SaveCore(input, create, actor, canModify, validate, projectOnly, null);

    public ProjectApiModel CreateProject(ProjectApiModel input, string actor, AccessControlApiModel owner, Action validate)
        => SaveCore(new RefDataApiModel { Project = input, Components = [] }, true, actor, _ => true,
            validate, true, owner).Project;

    private RefDataApiModel SaveCore(RefDataApiModel input, bool create, string actor,
        Func<int, bool> canModify, Action validate, bool projectOnly, AccessControlApiModel? owner)
    {
        if (!create && string.IsNullOrWhiteSpace(input.Revision)) throw new DefinitionPreconditionException(true);
        // Never retry an arbitrary successful mutation after an uncertain commit.
        using var strategyContext = factory.GetContext();
        return strategyContext.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var context = factory.GetContext();
            using var transaction = context.Database.BeginTransaction(IsolationLevel.Serializable);
            var project = create ? new Project { ObjectId = Guid.NewGuid() } :
                context.Projects.FromSqlRaw("SELECT * FROM [deploy].[Project] WITH (UPDLOCK,HOLDLOCK)")
                    .Single(p => p.Id == input.Project.ProjectId);
            if (!create && !canModify(project.Id)) throw new UnauthorizedAccessException();
            var before = create ? null : Read(context, project.Id);
            if (!create) RequireRevision(input.Revision, before!.Revision);
            ProjectTerraformBackends.ValidateChange(context, project, input.Project.TerraformStateBackends);
            ValidateNonSecret(input.Project.TerraformGitRepoUrl);
            ValidateNonSecret(input.Project.ArtefactsUrl);
            foreach (var component in Flatten(input.Components ?? []))
            {
                ValidateNonSecret(component.ScriptPath);
                ValidateKnownSensitiveArguments(context, component.ScriptPath);
            }
            validate();
            if (context.Projects.Any(p => p.Id != project.Id && p.Name == input.Project.ProjectName))
                throw new ArgumentException("A project with this name already exists.");
            if (!create && project.Name != input.Project.ProjectName &&
                Flatten(before!.Components).Any(c => c.ComponentType == ComponentType.Terraform))
                throw new ArgumentException("Renaming a project with Terraform definitions requires an operator-reviewed state procedure.");
            project.Name = input.Project.ProjectName;
            project.Description = input.Project.ProjectDescription;
            project.ArtefactsUrl = input.Project.ArtefactsUrl;
            project.ArtefactsSubPaths = input.Project.ArtefactsSubPaths;
            project.ArtefactsBuildRegex = input.Project.ArtefactsBuildRegex;
            project.TerraformGitRepoUrl = input.Project.TerraformGitRepoUrl;
            project.TerraformStateBackendsJson = JsonSerializer.Serialize(input.Project.TerraformStateBackends ?? []);
            project.LeanIXUrl = input.Project.LeanIXUrl;
            project.SourceControlType = input.Project.SourceControlType;
            if (create) context.Projects.Add(project);
            context.SaveChanges();
            if (create && owner != null)
                context.AccessControls.Add(new AccessControl
                {
                    ObjectId = project.ObjectId, Sid = owner.Sid, Pid = owner.Pid, Name = owner.Name,
                    Allow = owner.Allow, Deny = owner.Deny
                });
            if (!projectOnly)
            {
                var kept = new HashSet<int>();
                SaveTree(context, input.Components ?? [], project, null, kept, canModify);
                var removed = context.Components.Include(c => c.Projects)
                    .Where(c => c.Projects.Any(p => p.Id == project.Id) && !kept.Contains(c.Id)).ToList();
                foreach (var component in removed)
                    component.Projects.Remove(component.Projects.Single(p => p.Id == project.Id));
            }
            context.SaveChanges();
            var after = Read(context, project.Id);
            Record(context, before, after, actor, create ? "RefData.Create" : projectOnly ? "Project.Update" : "RefData.Update");
            context.SaveChanges();
            transaction.Commit();
            return after;
        });
    }

    private static void SaveTree(IDeploymentContext context, IEnumerable<ComponentApiModel> input,
        Project project, Component? parent, HashSet<int> kept, Func<int, bool> canModify)
    {
        foreach (var item in input)
        {
            ValidateNonSecret(item.ScriptPath);
            ValidateNonSecret(item.TerraformGitBranch);
            var existing = item.ComponentId.GetValueOrDefault() == 0 ? null :
                context.Components.Include(c => c.Projects).Include(c => c.Parent)
                    .Include(c => c.Script).ThenInclude(s => s!.Components)
                    .SingleOrDefault(c => c.Id == item.ComponentId);
            if (item.ComponentId.GetValueOrDefault() != 0 && existing == null)
                throw new ArgumentException("The component no longer exists.");
            if (existing != null && !existing.Projects.Any(p => p.Id == project.Id))
                throw new ArgumentException("The component does not belong to this project.");
            if (context.Components.Any(c => c.Id != (item.ComponentId ?? 0) && c.Name == item.ComponentName))
                throw new ArgumentException("A component with this name already exists. Choose another name.");
            var component = existing ?? new Component { ObjectId = Guid.NewGuid(), Projects = new List<Project> { project } };
            if (existing?.TerraformSourceType == TerraformSourceType.Catalog &&
                (component.Name != item.ComponentName || item.ComponentType != ComponentType.Terraform ||
                 item.TerraformSourceType != TerraformSourceType.Catalog ||
                 item.TerraformTemplateName != component.TerraformTemplateName ||
                 item.TerraformTemplateVersion != component.TerraformTemplateVersion))
                throw new ArgumentException("Catalog identity and pin require the explicit upgrade/state procedure.");
            // Component metadata is shared too. Do not make a project-local edit mutate
            // other project definitions without their owner's authorization.
            if (existing?.ComponentType == ComponentType.Terraform && component.Name != item.ComponentName)
                throw new ArgumentException("Renaming a Terraform component requires an operator-reviewed state procedure.");
            if (component.Projects.Any(p => p.Id != project.Id))
            {
                var shallow = JsonSerializer.Deserialize<ComponentApiModel>(JsonSerializer.Serialize(item))!;
                shallow.Children = [];
                if (JsonSerializer.Serialize(Map(component)) != JsonSerializer.Serialize(shallow))
                    throw new ArgumentException("This component belongs to multiple projects. Detach it or use an explicit shared operation.");
                if (!kept.Add(component.Id)) throw new ArgumentException("A component cannot occur twice in a tree.");
                SaveTree(context, item.Children ?? [], project, component, kept, canModify);
                continue;
            }
            component.Name = item.ComponentName;
            component.Parent = parent;
            component.IsEnabled = item.IsEnabled;
            component.StopOnFailure = item.StopOnFailure;
            component.ComponentType = item.ComponentType;
            component.TerraformSourceType = item.TerraformSourceType;
            component.TerraformGitBranch = item.TerraformGitBranch;
            component.TerraformSubPath = item.TerraformSubPath;
            component.TerraformTemplateName = item.TerraformTemplateName;
            component.TerraformTemplateVersion = item.TerraformTemplateVersion;
            var hasScript = item.ComponentType == ComponentType.PowerShell ||
                item.TerraformSourceType == TerraformSourceType.SharedFolder;
            if (hasScript && !string.IsNullOrEmpty(item.ScriptPath))
            {
                var old = component.Script;
                if (old == null || old.Path != item.ScriptPath || old.NonProdOnly != item.NonProdOnly ||
                    old.PowerShellVersionNumber != item.PSVersion.ToSafePsVersionString() || old.Name != item.ComponentName)
                {
                    var script = old != null && old.Components.Count == 1 ? old : new Script();
                    script.Name = item.ComponentName;
                    script.Path = item.ScriptPath;
                    script.NonProdOnly = item.NonProdOnly;
                    script.IsPathJSON = IsJson(item.ScriptPath);
                    script.PowerShellVersionNumber = item.PSVersion.ToSafePsVersionString();
                    component.Script = script;
                }
            }
            else { component.Script = null; component.ScriptId = null; }
            if (existing == null) context.Components.Add(component);
            context.SaveChanges();
            if (!kept.Add(component.Id)) throw new ArgumentException("A component cannot occur twice in a tree.");
            SaveTree(context, item.Children ?? [], project, component, kept, canModify);
        }
    }

    internal static ComponentApiModel Map(Component component) => new()
    {
        ComponentId = component.Id, ComponentName = component.Name, ParentId = component.Parent?.Id ?? 0,
        IsEnabled = component.IsEnabled, StopOnFailure = component.StopOnFailure,
        ComponentType = component.ComponentType, TerraformSourceType = component.TerraformSourceType,
        TerraformGitBranch = component.TerraformGitBranch, TerraformSubPath = component.TerraformSubPath,
        TerraformTemplateName = component.TerraformTemplateName, TerraformTemplateVersion = component.TerraformTemplateVersion,
        ScriptPath = component.Script?.Path ?? "", PSVersion = component.Script?.PowerShellVersionNumber,
        NonProdOnly = component.Script?.NonProdOnly ?? true, Children = new List<ComponentApiModel>()
    };

    internal static IEnumerable<ComponentApiModel> Flatten(IEnumerable<ComponentApiModel> components)
    {
        foreach (var component in components)
        {
            yield return component;
            foreach (var child in Flatten(component.Children ?? [])) yield return child;
        }
    }

    internal static Guid? Record(IDeploymentContext context, RefDataApiModel? before, RefDataApiModel after,
        string actor, string operation, string? sourceRecordId = null)
    {
        if (before?.Revision == after.Revision) return null;
        var change = new DeploymentDefinitionChange
        {
            Id = Guid.NewGuid(), ProjectId = after.Project.ProjectId, ProjectName = after.Project.ProjectName,
            Actor = actor, Operation = operation, OccurredAt = DateTimeOffset.UtcNow,
            BeforeJson = SafeJson(before), AfterJson = SafeJson(after), Revision = after.Revision, SourceRecordId = sourceRecordId,
            AffectedComponentsJson = JsonSerializer.Serialize(Flatten(before?.Components ?? [])
                .Concat(Flatten(after.Components)).GroupBy(c => c.ComponentId)
                .Select(g => new { g.Last().ComponentId, g.Last().ComponentName }))
        };
        context.DeploymentDefinitionChanges.Add(change);
        if (operation != "Project.Delete")
        {
            var actionType = before == null ? ActionType.Create : ActionType.Update;
            var auditAction = context.RefDataAuditActions.FirstOrDefault(a => a.Action == actionType);
            if (auditAction != null)
                context.RefDataAudits.Add(new RefDataAudit
                {
                    ChangeId = change.Id, ProjectId = after.Project.ProjectId, Action = auditAction,
                    Username = actor, Date = change.OccurredAt.UtcDateTime, Json = change.AfterJson
                });
        }
        if (before != null)
        {
            foreach (var previous in Flatten(before.Components))
            {
                Observe(context, before.Project, previous);
                if (Flatten(after.Components).Any(c => c.ComponentId == previous.ComponentId)) continue;
                var detached = SafeJson(new { SchemaVersion = 1, DetachedFromProject = after.Project.ProjectId, Component = previous });
                var revision = Hash(detached);
                if (!context.DeploymentDefinitionRevisions.Any(r => r.ProjectId == after.Project.ProjectId &&
                    r.ComponentId == previous.ComponentId && r.Revision == revision))
                    context.DeploymentDefinitionRevisions.Add(new DeploymentDefinitionRevision
                    {
                        Id = Guid.NewGuid(), ChangeId = change.Id, ProjectId = after.Project.ProjectId,
                        ComponentId = previous.ComponentId!.Value, ComponentName = previous.ComponentName,
                        DefinitionJson = detached, Revision = revision, ObservedAt = change.OccurredAt, IsBaseline = false
                    });
            }
        }
        foreach (var component in Flatten(after.Components))
            Observe(context, after.Project, component, change.Id);
        return change.Id;
    }

    internal static DeploymentDefinitionRevision Observe(IDeploymentContext context, ProjectApiModel project,
        ComponentApiModel component, Guid? changeId = null)
    {
        var json = SafeJson(new { SchemaVersion = 1, Project = project, Component = component });
        var revision = Hash(json);
        var found = context.DeploymentDefinitionRevisions.Local.FirstOrDefault(x =>
            x.ProjectId == project.ProjectId && x.ComponentId == component.ComponentId && x.Revision == revision) ??
            context.DeploymentDefinitionRevisions.SingleOrDefault(x =>
                x.ProjectId == project.ProjectId && x.ComponentId == component.ComponentId && x.Revision == revision);
        if (found != null) return found;
        var row = new DeploymentDefinitionRevision
        {
            Id = Guid.NewGuid(), ChangeId = changeId, ProjectId = project.ProjectId,
            ComponentId = component.ComponentId!.Value, ComponentName = component.ComponentName,
            DefinitionJson = json, Revision = revision, ObservedAt = DateTimeOffset.UtcNow, IsBaseline = changeId == null
        };
        context.DeploymentDefinitionRevisions.Add(row);
        return row;
    }

    private static bool IsJson(string value)
    {
        try { JsonNode.Parse(value); return true; }
        catch (JsonException) { return false; }
    }

    private static readonly Regex Sensitive = new(@"password|secret|token|credential|(?:^|_)key$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveArgument = new(@"(?i)(?:password|secret|token|credential)\s*[:=]\s*[^$\s]");

    internal static void ValidateKnownSensitiveArguments(IDeploymentContext context, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsJson(path)) return;
        var names = context.Properties.Where(p => p.Secure).Select(p => p.Name)
            .Concat(context.ConfigValues.Where(p => p.Secure).Select(p => p.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        void Visit(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var entry in obj)
                {
                    if (names.Contains(entry.Key) && entry.Value != null && !string.IsNullOrEmpty(entry.Value.ToString()) &&
                        !Regex.IsMatch(entry.Value.ToString(), @"^\$\{[A-Za-z0-9_.-]+\}$"))
                        throw new ArgumentException("A secure property argument must use a property reference, not a saved literal.");
                    Visit(entry.Value);
                }
            else if (node is JsonArray array) foreach (var item in array) Visit(item);
        }
        Visit(JsonNode.Parse(path));
    }

    public static void ValidateNonSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)))
            throw new ArgumentException("Credentials and sensitive URL arguments must use secure properties, not saved definitions.");
        if (SensitiveArgument.IsMatch(value))
            throw new ArgumentException("Sensitive literal arguments must be replaced by secure property references.");
        if (!IsJson(value)) return;
        ValidateNode(JsonNode.Parse(value));
    }

    private static void ValidateNode(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var entry in obj)
            {
                if (Sensitive.IsMatch(entry.Key) && entry.Value != null)
                {
                    var text = entry.Value.ToString();
                    if (!string.IsNullOrEmpty(text) && !Regex.IsMatch(text, @"^\$\{[A-Za-z0-9_.-]+\}$"))
                        throw new ArgumentException("Sensitive literal arguments must be replaced by secure property references.");
                }
                ValidateNode(entry.Value);
            }
        else if (node is JsonArray array) foreach (var item in array) ValidateNode(item);
        else if (node is JsonValue value && value.TryGetValue<string>(out var text) &&
            Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
            (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)))
            throw new ArgumentException("Credentials and sensitive URL arguments must use secure properties.");
    }

    internal static string SafeJson(object? value)
    {
        var node = JsonSerializer.SerializeToNode(value);
        Sanitize(node);
        return node?.ToJsonString() ?? "null";
    }

    private static void Sanitize(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                var child = obj[key];
                if (Sensitive.IsMatch(key) || key.Equals("GenericArguments", StringComparison.OrdinalIgnoreCase))
                { obj[key] = child == null ? null : "sha256:" + Hash(child.ToJsonString()); continue; }
                if (child is JsonValue val && val.TryGetValue<string>(out var text))
                {
                    if (IsJson(text))
                    {
                        var embedded = JsonNode.Parse(text); Sanitize(embedded); obj[key] = embedded?.ToJsonString();
                    }
                    else if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                        (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)))
                        obj[key] = uri.GetLeftPart(UriPartial.Path).Replace(uri.UserInfo + "@", "");
                    else if (SensitiveArgument.IsMatch(text)) obj[key] = "sha256:" + Hash(text);
                }
                else Sanitize(child);
            }
        else if (node is JsonArray array) foreach (var item in array) Sanitize(item);
    }
}

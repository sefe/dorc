using Dorc.ApiModel;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Extensions;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.SqlServer.Management.Smo.Agent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Component = Dorc.PersistentData.Model.Component;

namespace Dorc.PersistentData.Sources
{
    public enum HttpRequestType
    {
        Post,
        Put
    }

    public class ManageProjectsPersistentSource : IManageProjectsPersistentSource
    {
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly IRequestsPersistentSource _requestsPersistentSource;

        public ManageProjectsPersistentSource(IDeploymentContextFactory contextFactory, IRequestsPersistentSource requestsPersistentSource)
        {
            _requestsPersistentSource = requestsPersistentSource;
            _contextFactory = contextFactory;
        }

        public void InsertRefDataAudit(string username, HttpRequestType requestType, RefDataApiModel refDataApiModel)
        {
            using (var context = _contextFactory.GetContext())
            {
                var refDataAudit = new RefDataAudit
                {
                    Date = DateTime.Now,
                    Username = username,
                    Project = context.Projects.First(x => x.Id == refDataApiModel.Project.ProjectId),
                    Json = JsonSerializer.Serialize(refDataApiModel, new JsonSerializerOptions { WriteIndented = true })
                };

                switch (requestType)
                {
                    case HttpRequestType.Post:
                        refDataAudit.Action = context.RefDataAuditActions.First(x =>
                            x.Action == ActionType.Create);
                        break;

                    case HttpRequestType.Put:
                        refDataAudit.Action = context.RefDataAuditActions.First(x =>
                            x.Action == ActionType.Update);
                        break;
                }

                context.RefDataAudits.Add(refDataAudit);
                context.SaveChanges();
            }
        }

        public GetRefDataAuditListResponseDto GetRefDataAuditByProjectId(int projectId, int limit, int page, PagedDataOperators operators)
        {
            PagedModel<RefDataAudit>? output = null;
            using (var context = _contextFactory.GetContext())
            {
                var reqStatusesQueryable = context.RefDataAudits
                    .Include(refDataAudit => refDataAudit.Action)
                    .Include(refDataAudit => refDataAudit.Project)
                    .AsQueryable();

                var filterLambdas =
                    new List<Expression<Func<RefDataAudit, bool>>>();
                var projectIdExpr = reqStatusesQueryable.ContainsExpression(nameof(RefDataAudit.ProjectId),
                                projectId.ToString());
                if (projectIdExpr != null)
                    filterLambdas.Add(projectIdExpr);
                if (operators.Filters != null && operators.Filters.Any())
                {
                    foreach (var pagedDataFilter in operators.Filters)
                    {
                        if (pagedDataFilter == null)
                            continue;
                        if (!string.IsNullOrEmpty(pagedDataFilter.Path) && !string.IsNullOrEmpty(pagedDataFilter.FilterValue))
                        {
                            var filterExpr = reqStatusesQueryable.ContainsExpression(pagedDataFilter.Path,
                                    pagedDataFilter.FilterValue);
                            if (filterExpr != null)
                                filterLambdas.Add(filterExpr);
                        }
                    }
                }
                reqStatusesQueryable = WhereAll(reqStatusesQueryable, filterLambdas.ToArray());

                if (operators.SortOrders != null && operators.SortOrders.Any())
                {
                    IOrderedQueryable<RefDataAudit>? orderedQuery = null;

                    for (var i = 0; i < operators.SortOrders.Count; i++)
                    {
                        if (operators.SortOrders[i] == null)
                            continue;
                        if (string.IsNullOrEmpty(operators.SortOrders[i].Path) ||
                            string.IsNullOrEmpty(operators.SortOrders[i].Direction))
                            continue;

                        var param = Expression.Parameter(typeof(RefDataAudit), "RefDataAudit");
                        var prop = Expression.PropertyOrField(param, operators.SortOrders[i].Path);

                        switch (prop.Type)
                        {
                            case Type boolType when boolType == typeof(bool):
                                {
                                    var expr = GetExpressionForOrdering<bool>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type stringType when stringType == typeof(string):
                                {
                                    var expr = GetExpressionForOrdering<string>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type intType when intType == typeof(int):
                                {
                                    var expr = GetExpressionForOrdering<int>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                            case Type datetimeType when datetimeType == typeof(DateTime):
                                {
                                    var expr = GetExpressionForOrdering<DateTime>(prop, param);
                                    orderedQuery = OrderScripts(operators, i, orderedQuery, reqStatusesQueryable, expr);
                                    break;
                                }
                        }
                    }

                    if (orderedQuery != null)
                        output = orderedQuery.AsNoTracking()
                            .Paginate(page, limit);
                }

                if (output == null)
                    output = reqStatusesQueryable.AsNoTracking()
                        .OrderByDescending(s => s.Date)
                        .Paginate(page, limit);


                return new GetRefDataAuditListResponseDto
                {
                    CurrentPage = output.CurrentPage,
                    TotalPages = output.TotalPages,
                    TotalItems = output.TotalItems,
                    Items = output.Items.Select(refDataAudit => new RefDataAuditApiModel
                    {
                        RefDataAuditId = refDataAudit.RefDataAuditId,
                        ProjectId = refDataAudit.ProjectId,
                        Project = new ProjectApiModel
                        {
                            ProjectId = refDataAudit.Project.Id,
                            ArtefactsBuildRegex = refDataAudit.Project.ArtefactsBuildRegex,
                            ArtefactsSubPaths = refDataAudit.Project.ArtefactsSubPaths,
                            ArtefactsUrl = refDataAudit.Project.ArtefactsUrl,
                            ProjectDescription = refDataAudit.Project.Description,
                            ProjectName = refDataAudit.Project.Name
                        },
                        RefDataAuditActionId = refDataAudit.RefDataAuditActionId,
                        Action = refDataAudit.Action.Action.ToString(),
                        Username = refDataAudit.Username,
                        Date = refDataAudit.Date,
                        Json = refDataAudit.Json
                    }).ToList()
                };
            }
        }

        public IList<ComponentApiModel> GetOrderedComponents(int projectId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var topLevelParentComponents = new List<Component>();
                var allComponents = new List<ComponentApiModel>();
                var components = context.Projects
                    .Include(p => p.Components)
                    .ThenInclude(p => p.Script)
                    .FirstOrDefault(p => p.Id == projectId)?.Components;

                if (components != null)
                    foreach (var component in components)
                        topLevelParentComponents.Add(GetTopLevelParentComponent(component));

                topLevelParentComponents = topLevelParentComponents.Distinct().OrderBy(c => c.Name)
                    .Where(x => x.Projects.Any(p => p.Id == projectId)).ToList();

                GetChildComponentsInOrder(topLevelParentComponents, allComponents, projectId);

                return allComponents;
            }
        }

        public IList<ComponentApiModel> GetOrderedComponents(IEnumerable<string> components)
        {
            using (var context = _contextFactory.GetContext())
            {
                Dictionary<int, Component> topLevelParentComponents = new Dictionary<int, Component>();
                var allChildComponents = new List<Component>();

                var comps = context.Components
                    .Include(component => component.Children)
                    .Include(component => component.Parent)
                    .Include(component => component.Script)
                    .Where(c => components.Contains(c.Name));

                foreach (var component in comps)
                {
                    Component topLevelEnabledParentComponent = GetTopLevelEnabledParentComponent(component);

                    if (!topLevelParentComponents.ContainsKey(topLevelEnabledParentComponent.Id))
                    {
                        topLevelParentComponents.Add(topLevelEnabledParentComponent.Id, topLevelEnabledParentComponent);
                    }
                }

                GetChildComponentsInOrder(topLevelParentComponents.Values.OrderBy(c => c.Name), allChildComponents);

                return allChildComponents.Where(x => comps.Contains(x)).Select(MapToComponentApiModel).ToList();
            }
        }

        public void TraverseComponents(IEnumerable<ComponentApiModel> components, int? parentId, int projectId,
            Action<ComponentApiModel, int, int?> action)
        {
            if (components == null) return;
            foreach (var component in components)
            {
                action(component, projectId, parentId);
                TraverseComponents(component.Children, component.ComponentId, projectId, action);
            }
        }
        public void TraverseComponents(IEnumerable<ComponentApiModel> components, int projectId,
            Action<ComponentApiModel, int> action)
        {
            if (components == null) return;
            foreach (var component in components)
            {
                action(component, projectId);
                TraverseComponents(component.Children, projectId, action);
            }
        }

        public void FlattenApiComponents(IEnumerable<ComponentApiModel> components,
            IList<ComponentApiModel> flattenedComponents)
        {
            if (components == null) return;
            foreach (var component in components)
            {
                flattenedComponents.Add(component);
                if (component.Children != null) FlattenApiComponents(component.Children, flattenedComponents);
            }
        }

        public void ValidateComponents(IList<ComponentApiModel> components, int projectId,
            HttpRequestType httpRequestType)
        {
            var flattenedComponents = new List<ComponentApiModel>();
            FlattenApiComponents(components, flattenedComponents);
            foreach (var component in flattenedComponents)
            {
                ValidateAllComponentIdsAreZero(component, httpRequestType);
                ValidateComponentNameAndIdAreNotEmpty(component);
                ValidateComponentIdsDoNotBelongToOtherProject(component, projectId);
                ValidateComponentNameDoesNotBelongToDifferentProject(component, projectId);
                ValidateNameLengthRestrictions(component);
            }

            ValidateNoDuplicateComponentIdsOrNames(flattenedComponents);
        }

        public void CreateComponent(ComponentApiModel apiComponent, int projectId, int? parentId)
        {
            if (apiComponent.ComponentId == 0)
                using (var context = _contextFactory.GetContext())
                {
                    var duplicateComponent =
                        context.Components.FirstOrDefault(x =>
                            EF.Functions.Collate(x.Name, DeploymentContext.CaseInsensitiveCollation) ==
                            EF.Functions.Collate(apiComponent.ComponentName, DeploymentContext.CaseInsensitiveCollation));
                    if (duplicateComponent != null)
                    {
                        if (duplicateComponent.Parent != null)
                            duplicateComponent.Parent.Children.Remove(duplicateComponent);
                        duplicateComponent.Name = Guid.NewGuid().ToString();
                        duplicateComponent.Description = $"Changed via api on {DateTime.Now.ToShortDateString()}";
                        duplicateComponent.Projects.Remove(context.Projects.First(p => p.Id == projectId));
                        var script = duplicateComponent.Script;
                        if (script != null)
                            script.Name = $"{script.Name} {duplicateComponent.Name}";

                        context.SaveChanges();
                    }

                    var component = new Component
                    {
                        Name = apiComponent.ComponentName,
                        Parent = context.Components.FirstOrDefault(x => x.Id == parentId),
                        ObjectId = Guid.NewGuid(),
                        Description = "Created via API",
                        IsEnabled = apiComponent.IsEnabled,
                        ComponentType = apiComponent.ComponentType
                    };

                    if (apiComponent.ScriptPath != null)
                    {
                        var script = new Script
                        {
                            Name = apiComponent.ComponentName,
                            Path = apiComponent.ScriptPath,
                            NonProdOnly = apiComponent.NonProdOnly,
                            IsPathJSON = IsScriptPathJson(apiComponent.ScriptPath),
                            PowerShellVersionNumber = apiComponent.PSVersion.ToSafePsVersionString()
                        };

                        component.Script = script;
                    }

                    component.Projects = new List<Project> { context.Projects.First(x => x.Id == projectId) };
                    context.Components.Add(component);
                    context.SaveChanges();
                    apiComponent.ComponentId = component.Id;
                }
        }

        public void UpdateComponent(ComponentApiModel apiComponent, int projectId, int? parentId)
        {
            if (apiComponent.ComponentId == 0)
                return;

            using (var context = _contextFactory.GetContext())
            {
                var component = context.Components
                    .Include(c => c.Projects)
                    .Include(s => s.Script)
                    .ThenInclude(s => s!.Components)
                    .Include(c => c.Parent)
                    .First(x => x.Id == apiComponent.ComponentId);

                if (component.Projects.Count == 0)
                {
                    var projectToAdd = context.Projects.FirstOrDefault(x => x.Id == projectId);
                    if (projectToAdd != null)
                        component.Projects.Add(projectToAdd);
                }

                // Update ComponentType for all updates
                component.ComponentType = apiComponent.ComponentType;

                DuplicateComponent(apiComponent, component, context);

                if (component.Parent is null && parentId != null)
                    component.Parent = context.Components.First(x => x.Id == parentId);
                else if (component.Parent is not null && parentId == null)
                    component.Parent = null;
                else if (component.Parent is not null && parentId != null && component.Parent.Id != parentId)
                    component.Parent = context.Components.First(x => x.Id == parentId);

                // Handle script updates
                var oldScript = component.Script;
                var isScriptShared = oldScript != null && oldScript.Components.Count > 1;

                if (!string.IsNullOrEmpty(apiComponent.ScriptPath))
                {
                    // Component has a script path
                    if (oldScript != null)
                    {
                        // Check if script content is changing
                        var isScriptContentChanged = oldScript.Path != apiComponent.ScriptPath
                            || oldScript.NonProdOnly != apiComponent.NonProdOnly
                            || oldScript.Name != apiComponent.ComponentName
                            || oldScript.PowerShellVersionNumber != apiComponent.PSVersion.ToSafePsVersionString();

                        if (isScriptContentChanged)
                        {
                            if (isScriptShared)
                            {
                                // Script is shared with other components, create a new script
                                var newScript = new Script
                                {
                                    Name = apiComponent.ComponentName,
                                    Path = apiComponent.ScriptPath,
                                    NonProdOnly = apiComponent.NonProdOnly,
                                    IsPathJSON = IsScriptPathJson(apiComponent.ScriptPath),
                                    PowerShellVersionNumber = apiComponent.PSVersion.ToSafePsVersionString()
                                };
                                component.Script = newScript;
                            }
                            else
                            {
                                // Script is not shared, update it
                                oldScript.Name = apiComponent.ComponentName;
                                oldScript.Path = apiComponent.ScriptPath;
                                oldScript.NonProdOnly = apiComponent.NonProdOnly;
                                oldScript.IsPathJSON = IsScriptPathJson(apiComponent.ScriptPath);
                                oldScript.PowerShellVersionNumber = apiComponent.PSVersion.ToSafePsVersionString();
                            }
                        }
                        else
                        {
                            // Script content unchanged, only update name if needed
                            if (oldScript.Name != apiComponent.ComponentName)
                            {
                                if (isScriptShared)
                                {
                                    // Script is shared, create new script with updated name
                                    var newScript = new Script
                                    {
                                        Name = apiComponent.ComponentName,
                                        Path = apiComponent.ScriptPath,
                                        NonProdOnly = apiComponent.NonProdOnly,
                                        IsPathJSON = IsScriptPathJson(apiComponent.ScriptPath),
                                        PowerShellVersionNumber = apiComponent.PSVersion.ToSafePsVersionString()
                                    };
                                    component.Script = newScript;
                                }
                                else
                                {
                                    oldScript.Name = apiComponent.ComponentName;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Component didn't have a script, create new one
                        var script = new Script
                        {
                            Name = apiComponent.ComponentName,
                            Path = apiComponent.ScriptPath,
                            NonProdOnly = apiComponent.NonProdOnly,
                            IsPathJSON = IsScriptPathJson(apiComponent.ScriptPath),
                            PowerShellVersionNumber = apiComponent.PSVersion.ToSafePsVersionString()
                        };
                        component.Script = script;
                    }
                }
                else if (apiComponent.ScriptPath == null && oldScript != null)
                {
                    // Script is being removed from component
                    if (isScriptShared)
                    {
                        // Script is shared, just remove reference
                        component.Script = null;
                        component.ScriptId = null;
                    }
                    else
                    {
                        // Script is not shared, delete it
                        component.Script = null;
                        component.ScriptId = null;
                        context.Scripts.Remove(oldScript);
                    }
                }
                else if (component.Script != null)
                {
                    component.Script.PowerShellVersionNumber = null; // it is just a container for other components, no sense to have PS version for it
                }

                context.SaveChanges();
            }
        }

        private static void DuplicateComponent(ComponentApiModel apiComponent, Component component, IDeploymentContext context)
        {
            if (component.Name.Equals(apiComponent.ComponentName, StringComparison.OrdinalIgnoreCase))
                return;

            var duplicateComponent =
                context.Components.FirstOrDefault(x =>
                    EF.Functions.Collate(x.Name, DeploymentContext.CaseInsensitiveCollation) ==
                    EF.Functions.Collate(apiComponent.ComponentName, DeploymentContext.CaseInsensitiveCollation));
            if (duplicateComponent is not null)
            {
                duplicateComponent.Name = Guid.NewGuid().ToString();
                context.SaveChanges();
            }

            component.Name = apiComponent.ComponentName;
            component.StopOnFailure = apiComponent.StopOnFailure;
            component.ComponentType = apiComponent.ComponentType;
        }

        public void DeleteComponents(IList<ComponentApiModel> apiComponents, int projectId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var components = context.Components.Include(c => c.Projects).Where(x => x.Projects.Any(p => p.Id == projectId)).ToList();
                var componentsToDelete = components.Where(x => apiComponents.All(y => y.ComponentId != x.Id));
                foreach (var component in componentsToDelete)
                {
                    component.Description = "ProjectId:" + projectId;
                    foreach (var project in component.Projects.ToArray())
                    {
                        if (component.Parent is not null)
                            component.Parent.Children.Remove(component);

                        component.Projects.Remove(project);
                    }
                }

                context.SaveChanges();
            }
        }

        public ReleaseInformationApiModel GetRequestDetails(int requestId)
        {
            var deploymentRequest = _requestsPersistentSource.GetRequest(requestId);
            if (deploymentRequest == null)
                return new ReleaseInformationApiModel();

            var releaseInfo = new ReleaseInformationApiModel
            {
                Build = deploymentRequest.BuildNumber,
                Components = deploymentRequest.Components?.Split('|') ?? Array.Empty<string>(),
                Project = deploymentRequest.Project
            };
            return releaseInfo;
        }

        public bool GetStatusOfRequest(int requestId)
        {
            var request = _requestsPersistentSource.GetRequest(requestId);
            return request?.Status?.Equals("Complete") ?? false;
        }

        private void GetChildComponentsInOrder(IEnumerable<Component> components,
            ICollection<Component> allChildComponents)
        {
            if (components == null) return;

            foreach (var component in components)
            {
                IList<Component> loadedChildren = new List<Component>();
                using (var context = _contextFactory.GetContext())
                {
                    loadedChildren = context.Components
                        .Where(c => c.Parent != null && c.Parent.Id == component.Id)
                        .ToList();

                    component.Children = loadedChildren;
                }

                allChildComponents.Add(component);
                GetChildComponentsInOrder(component.Children.OrderBy(c => c.Name), allChildComponents);
            }
        }

        private Component GetTopLevelEnabledParentComponent(Component component)
        {
            return component.Parent is null
                || !component.Parent.IsEnabled
                ? component
                : GetTopLevelEnabledParentComponent(component.Parent);
        }

        private static Component GetTopLevelParentComponent(Component component)
        {
            return component.Parent is null ? component : GetTopLevelParentComponent(component.Parent);
        }

        private static void GetChildComponentsInOrder(IList<Component> efComponents, IList<ComponentApiModel> apiComponents,
            int projectId)
        {
            if (efComponents == null) return;

            for (var i = 0; i < efComponents.Count; ++i)
            {
                apiComponents.Add(MapToComponentApiModel(efComponents[i]));
                apiComponents[i].Children = new List<ComponentApiModel>();
                GetChildComponentsInOrder(
                    efComponents[i].Children.OrderBy(c => c.Name).Where(x => x.Projects.Any(p => p.Id == projectId))
                        .ToList(), apiComponents[i].Children, projectId);
            }
        }

        private static ComponentApiModel MapToComponentApiModel(Component comp)
        {
            var script = comp.Script;
            if (script != null)
                return new ComponentApiModel
                {
                    ComponentId = comp.Id,
                    ComponentName = comp.Name,
                    ScriptPath = script.Path,
                    NonProdOnly = script.NonProdOnly,
                    StopOnFailure = comp.StopOnFailure,
                    IsEnabled = comp.IsEnabled,
                    ParentId = comp.Parent?.Id ?? 0,
                    ComponentType = comp.ComponentType,
                    PSVersion = script.PowerShellVersionNumber
                };

            return new ComponentApiModel
            {
                ComponentId = comp.Id,
                ComponentName = comp.Name,
                ScriptPath = string.Empty,
                NonProdOnly = true,
                StopOnFailure = comp.StopOnFailure,
                IsEnabled = comp.IsEnabled,
                ParentId = comp.Parent?.Id ?? 0,
                ComponentType = comp.ComponentType
            };
        }



        private static void ValidateAllComponentIdsAreZero(ComponentApiModel component, HttpRequestType httpRequestType)
        {
            if (httpRequestType != HttpRequestType.Post)
                return;

            if (component.ComponentId != 0)
                throw new ArgumentOutOfRangeException(nameof(component), "In a Post Call, all component ids should be 0");
        }

        private static void ValidateNameLengthRestrictions(ComponentApiModel component)
        {
            if (component.ComponentName.Length > 64)
                throw new ArgumentOutOfRangeException(nameof(component), "Component '" + component.ComponentName + "' must be no longer than 64 characters");
        }

        private static void ValidateComponentNameAndIdAreNotEmpty(ComponentApiModel component)
        {
            if (component.ComponentName == null) throw new ArgumentOutOfRangeException(nameof(component), "Component Name can not be null");
            if (component.ComponentId == null) throw new ArgumentOutOfRangeException(nameof(component), "Component Id can not be null");
        }

        private void ValidateComponentNameDoesNotBelongToDifferentProject(ComponentApiModel apiComponent, int projectId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var component = context.Components
                    .Include(c => c.Projects)
                    .FirstOrDefault(x =>
                        EF.Functions.Collate(x.Name, DeploymentContext.CaseInsensitiveCollation) ==
                        EF.Functions.Collate(apiComponent.ComponentName, DeploymentContext.CaseInsensitiveCollation));

                if (component is null)
                    return;

                if (component.Projects.Count == 0)
                {
                    if (component.Description != null && !component.Description.Equals("ProjectId:" + projectId,
                            StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentOutOfRangeException(nameof(apiComponent), "component id: " + apiComponent.ComponentName +
                                            " Belongs to other Project");
                }
                else
                {
                    foreach (var project in component.Projects)
                        if (project.Id != projectId)
                            throw new ArgumentOutOfRangeException(nameof(apiComponent), "component Name: " + apiComponent.ComponentName +
                                                " Belongs to Project: " + project.Name);
                }
            }
        }

        private void ValidateNoDuplicateComponentIdsOrNames(IEnumerable<ComponentApiModel> components)
        {
            foreach (var component in components)
            {
                if (components.Count(x =>
                        x.ComponentName.Equals(component.ComponentName, StringComparison.OrdinalIgnoreCase)) >
                    1)
                    throw new ArgumentOutOfRangeException(nameof(components), "component Name: '" + component.ComponentName + "' is listed multiple times");
                if (component.ComponentId != 0)
                    if (components.Count(x => x.ComponentId.Equals(component.ComponentId)) > 1)
                        throw new ArgumentOutOfRangeException(nameof(components), "component Id: " + component.ComponentId + " is listed multiple times");
            }
        }

        private void ValidateComponentIdsDoNotBelongToOtherProject(ComponentApiModel apiComponent, int projectId)
        {
            if (apiComponent.ComponentId == 0)
                return;

            using (var context = _contextFactory.GetContext())
            {
                var componentId = apiComponent.ComponentId ?? 0;
                var component = context.Components
                    .Include(c => c.Projects)
                    .FirstOrDefault(x => x.Id.Equals(componentId));

                if (component is not null)
                    if (component.Projects.Count == 0 && component.Description != null)
                    {
                        if (!component.Description.Equals("ProjectId:" + projectId,
                                StringComparison.OrdinalIgnoreCase))
                            throw new ArgumentOutOfRangeException(nameof(apiComponent), "component id: " + apiComponent.ComponentId +
                                                " Belongs to other Project");
                    }
                    else
                    {
                        foreach (var project in component.Projects)
                            if (project.Id != projectId)
                                throw new ArgumentOutOfRangeException(nameof(component), "component Name: " + apiComponent.ComponentName + //here
                                                    " Belongs to other Project: " + project.Name);
                    }
                else
                    throw new ArgumentOutOfRangeException(nameof(component), "component Id: " + apiComponent.ComponentId +
                                        " can not be located in database");
            }
        }

        private static bool IsScriptPathJson(string scriptPath)
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
                return false;
            try
            {
                JsonNode.Parse(scriptPath);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static IOrderedQueryable<RefDataAudit>? OrderScripts<T>(PagedDataOperators operators, int i, IOrderedQueryable<RefDataAudit>? orderedQuery,
            IQueryable<RefDataAudit> scriptsQuery, Expression<Func<RefDataAudit, T>> expr)
        {
            if (i == 0)
                switch (operators.SortOrders[i].Direction)
                {
                    case "asc":
                        orderedQuery = scriptsQuery.OrderBy(expr);
                        break;

                    case "desc":
                        orderedQuery = scriptsQuery.OrderByDescending(expr);
                        break;
                }
            else if (orderedQuery != null)
                switch (operators.SortOrders[i].Direction)
                {
                    case "asc":
                        orderedQuery = orderedQuery.ThenBy(expr);
                        break;

                    case "desc":
                        orderedQuery = orderedQuery.ThenByDescending(expr);
                        break;
                }

            return orderedQuery;
        }

        private static Expression<Func<RefDataAudit, R>> GetExpressionForOrdering<R>(MemberExpression prop, ParameterExpression param)
        {
            return Expression.Lambda<Func<RefDataAudit, R>>(prop, param);
        }

        private IQueryable<T> WhereAll<T>(
            IQueryable<T> source,
            params Expression<Func<T, bool>>[] predicates)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (predicates == null) throw new ArgumentNullException(nameof(predicates));
            if (predicates.Length == 0) return source.Where(x => false); // no matches!
            if (predicates.Length == 1) return source.Where(predicates[0]); // simple

            Expression<Func<T, bool>>? pred = null;
            for (var i = 0; i < predicates.Length; i++)
            {
                pred = pred is null
                    ? predicates[i]
                    : pred.And(predicates[i]);
            }
            return source.Where(pred!);
        }
    }
}
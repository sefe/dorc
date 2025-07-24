using Microsoft.EntityFrameworkCore;
using System.Security.Principal;
using log4net;
using Environment = Dorc.PersistentData.Model.Environment;
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Contexts;

namespace Dorc.PersistentData.Sources
{
    public class ProjectsPersistentSource : IProjectsPersistentSource
    {
        private const string FileProtocolPrefix = "file://";
        private const string HttpProtocolPrefix = "http://";
        private const string HttpsProtocolPrefix = "https://";
        private readonly IDeploymentContextFactory _contextFactory;
        private readonly IEnvironmentsPersistentSource _environmentsPersistentSource;
        private readonly ILog _logger;
        private readonly IClaimsPrincipalReader _claimsPrincipalReader;

        public ProjectsPersistentSource(IDeploymentContextFactory contextFactory,
            IEnvironmentsPersistentSource environmentsPersistentSource,
            ILog logger,
            IClaimsPrincipalReader claimsPrincipalReader
            )
        {
            _logger = logger;
            _environmentsPersistentSource = environmentsPersistentSource;
            _contextFactory = contextFactory;
            _claimsPrincipalReader = claimsPrincipalReader;
        }

        public IEnumerable<ProjectApiModel> GetProjects(IPrincipal user, int deprecated = 0)
        {
            using (var context = _contextFactory.GetContext())
            {
                if (deprecated == 0)
                {
                    var projects = context.Projects
                        .Where(project => !project.Name.Contains("Deprecated"))
                        .OrderBy(project => project.Name).ToList();

                    return projects.Select(MapToProjectApiModel).ToList();
                }
                else
                {
                    var projects = context.Projects
                        .OrderBy(p => p.Name).ToList();

                    return projects.Select(MapToProjectApiModel).ToList();
                }
            }
        }

        public ProjectApiModel GetProject(string projectName)
        {
            using (var context = _contextFactory.GetContext())
            {
                var single = context.Projects.FirstOrDefault(p => p.Name.Equals(projectName));
                if (single != null && single.SourceDatabaseId != null && single.SourceDatabaseId != 0)
                {
                    single.SourceDatabase = context.Databases.FirstOrDefault(d => d.Id == single.SourceDatabaseId);
                }
                return MapToProjectApiModel(single);
            }
        }

        public IEnumerable<ComponentApiModel> GetComponentsForProject(int projectId)
        {
            if (projectId <= 0)
                return new List<ComponentApiModel>();

            var projectApiModel = GetProject(projectId);
            return GetComponentsForProject(projectApiModel.ProjectName);

        }
        public IEnumerable<ComponentApiModel> GetComponentsForProject(string projectName)
        {
            using (var context = _contextFactory.GetContext())
            {
                var components = context.Components
                    .Include(component => component.Children)
                    .Include(component => component.Projects)
                    .Include(component => component.Script)
                    .Where(component => component.Projects.Any(project => project.Name.Equals(projectName))).ToList()
                    .Select(component => ComponentsPersistentSource.MapToComponentApiModel(component, context))
                    .Where(componentApiModel => componentApiModel != null).ToList();

                if (components.Any())
                {
                    return components;
                }

                return components;
            }
        }

        public Project GetSecurityObject(string projectName)
        {
            using (var context = _contextFactory.GetContext())
            {
                var single = context.Projects
                    
                    .First(project => project.Name.Equals(projectName));
                return single;
            }
        }

        public Project GetSecurityObject(int projectId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var single = context.Projects
                    
                    .First(project => project.Id.Equals(projectId));
                return single;
            }
        }

        public ProjectApiModel? GetProject(int projectId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var single = context.Projects
                    
                    .Single(project => project.Id == projectId);

                if (single.SourceDatabaseId != null && single.SourceDatabaseId != 0)
                {
                    single.SourceDatabase = context.Databases.FirstOrDefault(d => d.Id == single.SourceDatabaseId);
                }
                return MapToProjectApiModel(single);
            }
        }

        public IEnumerable<EnvironmentApiModel> GetProjectEnvironments(int projectId, IPrincipal user)
        {
            using (var context = _contextFactory.GetContext())
            {
                var environments = context.Environments
                    .Include(environment => environment.Projects)
                    .Where(environment => environment.Projects
                        .Any(project => project.Id == projectId))
                    .OrderBy(environment => environment.Name)
                    .Select(MapToEnvironmentApiModel).ToList();

                return environments;
            }
        }

        public TemplateApiModel<EnvironmentApiModel>? GetProjectEnvironments(
            string projectName,
            IPrincipal user,
            bool includeRead)
        {
            using (var context = _contextFactory.GetContext())
            {
                var project = context.Projects
                    .Include(project => project.Environments)
                    .FirstOrDefault(project => project.Name.Equals(projectName));

                if (project is null) return null;

                var accessLevel = AccessLevel.Write;
                if (includeRead)
                    accessLevel = AccessLevel.None;

                var accessibleEnvironmentsAccessLevel = _environmentsPersistentSource.AccessibleEnvironmentsAccessLevel(
                    context, project.Name, user, accessLevel);

                return new TemplateApiModel<EnvironmentApiModel>
                {
                    Items = accessibleEnvironmentsAccessLevel.ToList().Select(data =>
                        _environmentsPersistentSource.MapToEnvironmentApiModel(data.Environment, data.UserEditable,
                            data.IsOwner)).ToList(),
                    Project = MapToProjectApiModel(project),
                };
            }
        }

        public void InsertProject(ProjectApiModel apiProject)
        {
            using (var context = _contextFactory.GetContext())
            {
                var project = new Project
                {
                    Name = apiProject.ProjectName,
                    Description = apiProject.ProjectDescription,
                    ObjectId = Guid.NewGuid()
                };

                if (ProjectArtifactsUriHttpValid(apiProject))
                {
                    project.ArtefactsUrl = apiProject.ArtefactsUrl;
                    project.ArtefactsSubPaths = apiProject.ArtefactsSubPaths;
                    project.ArtefactsBuildRegex = apiProject.ArtefactsBuildRegex;
                }
                else
                {
                    if (ProjectArtifactsUriFileValid(apiProject))
                        project.ArtefactsUrl = apiProject.ArtefactsUrl;
                }

                context.Projects.Add(project);
                context.SaveChanges();

                apiProject.ProjectId = project.Id;
            }
        }

        public int GetProjectId(string projectName)
        {
            using (var context = _contextFactory.GetContext())
            {
                var project = context.Projects.FirstOrDefault(a => a.Name == projectName);

                return project is not null ? project.Id : 0;
            }
        }

        public void UpdateProject(ProjectApiModel newProjectDetails)
        {
            //check if ProjectName has changed
            using (var context = _contextFactory.GetContext())
            {
                var currentProj = context.Projects.First(x => x.Id == newProjectDetails.ProjectId);

                if (!currentProj.Name.Equals(newProjectDetails.ProjectName, StringComparison.OrdinalIgnoreCase))
                    currentProj.Name = newProjectDetails.ProjectName;

                currentProj.Description = newProjectDetails.ProjectDescription;

                if (ProjectArtifactsUriHttpValid(newProjectDetails) || ProjectArtifactsUriFileValid(newProjectDetails))
                {
                    currentProj.ArtefactsUrl = newProjectDetails.ArtefactsUrl;
                    currentProj.ArtefactsSubPaths = newProjectDetails.ArtefactsSubPaths;
                    currentProj.ArtefactsBuildRegex = newProjectDetails.ArtefactsBuildRegex;
                }

                else
                {
                    throw new ArgumentOutOfRangeException(nameof(newProjectDetails),
                        "Unable to validate URL as either file or http(s)");
                }

                context.SaveChanges();
            }
        }

        public bool RemoveEnvironmentMappingFromProject(string project, string environment, IPrincipal user)
        {
            using (var context = _contextFactory.GetContext())
            {
                try
                {
                    var projects = context.Projects.Include(p => p.Environments).FirstOrDefault(p => p.Name == project);
                    if (projects is null)
                        return false;

                    var projectsEnvironments = projects.Environments;

                    var env = projectsEnvironments.FirstOrDefault(e => e.Name == environment);
                    if (env is null)
                        return false;

                    string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                    EnvironmentHistoryPersistentSource.AddHistory(env.Name, string.Empty,
                        "Environment removed from project " + projects.Name,
                        username, "Remove Environment Mapping From Project", context);

                    projectsEnvironments.Remove(env);
                    context.SaveChanges();
                    return true;
                }
                catch (Exception e)
                {

                    Console.WriteLine(e);
                    throw;
                }
            }
        }

        public bool AddEnvironmentMappingToProject(string project, string environment, IPrincipal user)
        {
            using (var context = _contextFactory.GetContext())
            {
                try
                {
                    var proj = context.Projects.FirstOrDefault(p => p.Name == project);

                    if (proj is null)
                        return false;

                    var env = context.Environments.First(e => e.Name == environment);

                    proj.Environments.Add(env);

                    string username = _claimsPrincipalReader.GetUserFullDomainName(user);
                    EnvironmentHistoryPersistentSource.AddHistory(env.Name, string.Empty,
                        "Environment added to project " + proj.Name,
                        username, "Add Environment Mapping To Project", context);

                    context.SaveChanges();
                    return true;

                }
                catch (Exception e)
                {
                    var errMsg = $"Unable to map environment '{environment}' to '{project}'";
                    _logger.Error(errMsg, e);
                    throw new ArgumentOutOfRangeException(errMsg, e);
                }
            }
        }

        public void ValidateProject(ProjectApiModel apiProject, HttpRequestType httpRequestType)
        {
            ValidateProjectIdExists(apiProject, httpRequestType);
            ValidateProjectNameDoesNotBelongToOtherProject(apiProject);
            ValidateProjectNameIsNotNullOrEmpty(apiProject);
            ValidateProjectHasUrl(apiProject);
            ValidateProjectLengthRestrictions(apiProject);
        }

        public bool ProjectArtifactsUriFileValid(ProjectApiModel apiProject)
        {
            return apiProject.ArtefactsUrl.StartsWith(FileProtocolPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public bool ProjectArtifactsUriHttpValid(ProjectApiModel apiProject)
        {
            return apiProject.ArtefactsUrl.StartsWith(HttpProtocolPrefix, StringComparison.OrdinalIgnoreCase)
                || apiProject.ArtefactsUrl.StartsWith(HttpsProtocolPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private void ValidateProjectHasUrl(ProjectApiModel apiProject)
        {
            if (string.IsNullOrEmpty(apiProject.ArtefactsUrl)) throw new ArgumentOutOfRangeException(nameof(apiProject), "Project URL can not be empty");

            if (ProjectArtifactsUriHttpValid(apiProject))
            {
                if (string.IsNullOrEmpty(apiProject.ArtefactsSubPaths))
                    throw new ArgumentOutOfRangeException(nameof(apiProject), "Azure DevOps Server URL / File Path can not be null or empty");
                if (apiProject.ArtefactsBuildRegex == null)
                    throw new ArgumentOutOfRangeException(nameof(apiProject), "Build Definition Regex can not be empty"); // can it be empty and not null?
            }
            else if (!ProjectArtifactsUriFileValid(apiProject))
            {
                throw new ArgumentOutOfRangeException(nameof(apiProject), "URL must either begin with http(s):// or file://");
            }
        }

        private void ValidateProjectLengthRestrictions(ProjectApiModel apiProject)
        {
            if (apiProject.ProjectName.Length > 64)
                throw new ArgumentOutOfRangeException(nameof(apiProject),
                    "Project Name '" + apiProject.ProjectName + "' must be no longer than 64 characters");

            if (apiProject.ArtefactsUrl.Length > 512)
                throw new ArgumentOutOfRangeException(nameof(apiProject), "Project URL '" + apiProject.ArtefactsUrl + "' must be no longer than 512 characters");

            if (ProjectArtifactsUriHttpValid(apiProject))
                if (apiProject.ArtefactsSubPaths.Length > 512)
                    throw new ArgumentOutOfRangeException(nameof(apiProject), "Azure DevOps Project '" + apiProject.ArtefactsSubPaths +
                                        "' must be no longer than 64 characters");
        }

        private void ValidateProjectIdExists(ProjectApiModel apiProject, HttpRequestType httpRequestType)
        {
            if (httpRequestType == HttpRequestType.Put)
                using (var context = _contextFactory.GetContext())
                {
                    var project = context.Projects.FirstOrDefault(x => x.Id == apiProject.ProjectId);

                    if (project is null)
                        throw new ArgumentOutOfRangeException(nameof(apiProject), "Project not found for project Id " + apiProject.ProjectId);
                }
            else if (httpRequestType == HttpRequestType.Post)
                if (apiProject.ProjectId != 0)
                    throw new ArgumentOutOfRangeException(nameof(apiProject), "Project Id must equal 0 for a Post Call");
        }

        private void ValidateProjectNameDoesNotBelongToOtherProject(ProjectApiModel apiProject)
        {
            using (var context = _contextFactory.GetContext())
            {
                var project = context.Projects.FirstOrDefault(project =>
                    EF.Functions.Collate(project.Name, DeploymentContext.CaseInsensitiveCollation)
                        == EF.Functions.Collate(apiProject.ProjectName, DeploymentContext.CaseInsensitiveCollation));
                if (project is not null)
                    if (project.Id != apiProject.ProjectId)
                        throw new ArgumentOutOfRangeException(nameof(apiProject), "Project Name Belongs to project id:" + project.Id);
            }
        }

        private static void ValidateProjectNameIsNotNullOrEmpty(ProjectApiModel apiProject)
        {
            if (string.IsNullOrEmpty(apiProject.ProjectName))
                throw new ArgumentOutOfRangeException(nameof(apiProject), "Project Name can not be null or empty");
        }

        internal static ProjectApiModel? MapToProjectApiModel(Project project)
        {
            if (project is null) return null;

            return new ProjectApiModel
            {
                ProjectId = project.Id,
                ProjectDescription = project.Description,
                ProjectName = project.Name,
                ArtefactsSubPaths = project.ArtefactsSubPaths,
                ArtefactsUrl = project.ArtefactsUrl,
                ArtefactsBuildRegex = project.ArtefactsBuildRegex,
                SourceDatabase = project.SourceDatabase != null ? DatabasesPersistentSource.MapToDatabaseApiModel(project.SourceDatabase) : null
            };
        }

        private static EnvironmentApiModel MapToEnvironmentApiModel(Environment env)
        {
            return new EnvironmentApiModel
            {
                EnvironmentId = env.Id,
                EnvironmentName = env.Name,
                EnvironmentIsProd = env.IsProd,
                EnvironmentSecure = env.Secure
            };
        }

        public void DeleteProject(int projectId)
        {
            using (var context = _contextFactory.GetContext())
            {
                var project = context.Projects
                    .Include(p => p.Components)
                    .Include(p => p.Environments)
                    .Include(p => p.RefDataAudits)
                    .FirstOrDefault(p => p.Id == projectId);

                if (project == null)
                    throw new ArgumentException($"Project with ID {projectId} not found.");

                // Remove relationships before deleting the project
                project.Components.Clear();
                project.Environments.Clear();
                
                // Delete RefDataAudits associated with this project
                foreach (var audit in project.RefDataAudits.ToList())
                {
                    context.RefDataAudits.Remove(audit);
                }

                // Remove the project itself
                context.Projects.Remove(project);
                context.SaveChanges();
            }
        }

    }
}
using Dorc.ApiModel;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Api.Windows.Orchestration
{
    /// <summary>
    /// Orchestrates project and component-related operations
    /// </summary>
    public class ProjectComponentOrchestrator
    {
        private readonly IManageProjectsPersistentSource _manageProjectsPersistentSource;
        private readonly ILogger _log;
        private readonly IProjectsPersistentSource _projectsPersistentSource;

        public ProjectComponentOrchestrator(
            IManageProjectsPersistentSource manageProjectsPersistentSource,
            IProjectsPersistentSource projectsPersistentSource,
            ILogger<ProjectComponentOrchestrator> log)
        {
            _projectsPersistentSource = projectsPersistentSource;
            _log = log;
            _manageProjectsPersistentSource = manageProjectsPersistentSource;
        }

        public TemplateApiModel<ComponentApiModel> GetComponentsByProject(string projectName)
        {
            try
            {
                var project = _projectsPersistentSource.GetProject(projectName);
                if (project == null)
                    throw new InvalidDataException($"Unable to locate '{projectName}' in DOrc!");

                var components = _projectsPersistentSource.GetComponentsForProject(projectName);

                return new TemplateApiModel<ComponentApiModel> { Project = project, Items = components };
            }
            catch (Exception e)
            {
                _log.LogError(e, "GetComponentsByProject");
                throw;
            }
        }

        public List<ReleaseInformationApiModel> GetReleaseInformation(IEnumerable<int> requestIds)
        {
            var orderedRequestIds = requestIds.OrderBy(x => x);
            var listOfReleaseInformation = new List<ReleaseInformationApiModel>();
            foreach (var id in orderedRequestIds)
                listOfReleaseInformation.Add(_manageProjectsPersistentSource.GetRequestDetails(id));
            return listOfReleaseInformation;
        }
    }
}

using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IEnvironmentTemplatesPersistentSource
    {
        IEnumerable<EnvironmentTemplateApiModel> GetTemplatesForProject(int projectId);
        EnvironmentTemplateApiModel? GetTemplateById(int templateId);
        EnvironmentTemplateApiModel? GetTemplateByName(int projectId, string name);
        EnvironmentTemplateApiModel CreateTemplate(EnvironmentTemplateApiModel model, string username);
        EnvironmentTemplateApiModel UpdateTemplate(EnvironmentTemplateApiModel model, string username);
        bool DeleteTemplate(int templateId);
    }
}

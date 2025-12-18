using Dorc.ApiModel;

namespace Dorc.PersistentData.Sources.Interfaces
{
    public interface IComponentsPersistentSource
    {
        ComponentApiModel? GetComponentByName(string componentName);

        void LoadChildren(ComponentApiModel component);

        void SaveEnvComponentStatus(int environmentId, ComponentApiModel component,
            string resultStatus, int requestId);

        ScriptApiModel? GetScripts(int componentId);
    }
}
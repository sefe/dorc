using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.Core.VariableResolution
{
    public interface IVariableResolver
    {
        IDictionary<string, VariableValue> LoadProperties();
        void SetPropertyValue(string property, VariableValue? value);
        void SetPropertyValue(string property, string value);
        VariableValue? GetPropertyValue(string property);
        bool PropertiesLoaded();
        IDictionary<string, VariableValue> LocalProperties();
    }
}
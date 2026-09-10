using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.Core.VariableResolution
{
    public interface IVariableResolver
    {
        IDictionary<string, VariableValue> LoadProperties();
        void SetPropertyValue(string property, VariableValue? value);
        void SetPropertyValue(string property, string value);

        /// <summary>
        /// Records a property whose value was supplied on the deployment request rather than
        /// by an administrator.
        ///
        /// Such values, and any curated value that interpolates one, are never passed to the
        /// expression evaluator: that evaluator compiles and runs C#, so evaluating a
        /// request-supplied string would let anyone who can submit a request execute code
        /// inside the Monitor process - which holds both deployment credential pairs.
        /// Setting the same property through the ordinary accessors does not confer this
        /// protection, so request-handling code must use this method.
        /// </summary>
        void SetRequestSuppliedPropertyValue(string property, string value);
        VariableValue? GetPropertyValue(string property);
        bool PropertiesLoaded();
        IDictionary<string, VariableValue> LocalProperties();
    }
}
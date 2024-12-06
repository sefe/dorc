using Dorc.ApiModel;

namespace Dorc.Core.VariableResolution
{
    public class BundledRequestVariableLoader : IBundledRequestVariableLoader
    {
        private List<RequestProperty> _variables;

        public void SetVariables(List<RequestProperty> variables)
        {
            _variables = variables;
        }

        public List<RequestProperty> GetVariables()
        {
            return _variables;
        }
    }
}

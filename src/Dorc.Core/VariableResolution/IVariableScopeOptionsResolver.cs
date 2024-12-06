using Dorc.ApiModel;

namespace Dorc.Core.VariableResolution
{
    public interface IVariableScopeOptionsResolver
    {
        void SetPropertyValues(IVariableResolver variableResolver);
        void SetPropertyValues(IVariableResolver variableResolver, EnvironmentApiModel environment);
    }
}
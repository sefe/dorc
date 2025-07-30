using Dorc.ApiModel;

namespace Dorc.Core.VariableResolution
{
    public interface IVariableScopeOptionsResolver
    {
        void SetPropertyValues(IVariableResolver variableResolver, EnvironmentApiModel environment);
        void SetPropertyValues(IVariableResolver variableResolver, EnvironmentApiModel environment, DeploymentRequestApiModel? deploymentRequest);
    }
}
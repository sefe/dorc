using Dorc.ApiModel;
using System.Security.Principal;

namespace Dorc.Core.VariableResolution
{
    public interface IVariableScopeOptionsResolver
    {
        void SetPropertyValues(IVariableResolver variableResolver, EnvironmentApiModel environment);

        void SetGlobalPropertyValues(IVariableResolver variableResolver, IPrincipal user);
    }
}
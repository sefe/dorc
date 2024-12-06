using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.Core.VariableResolution;

public interface IPropertyEvaluator
{
    VariableValue? Evaluate(IVariableResolver variableResolver, string value);
}
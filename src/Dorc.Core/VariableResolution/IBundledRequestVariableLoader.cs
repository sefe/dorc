using Dorc.ApiModel;

namespace Dorc.Core.VariableResolution;

public interface IBundledRequestVariableLoader
{
    void SetVariables(List<RequestProperty> variables);
        
    List<RequestProperty> GetVariables();
}
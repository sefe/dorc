namespace Dorc.Core.VariableResolution;

public interface IPropertyExpressionEvaluator
{
    object Evaluate(object value);
}
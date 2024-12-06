using System.Collections.Concurrent;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.Core.VariableResolution
{
    public class BundledRequestVariableResolver : IVariableResolver
    {
        private readonly IPropertyEvaluator _evaluator;
        private readonly IPropertyExpressionEvaluator _expressionEvaluator;

        private readonly ConcurrentDictionary<string, VariableValue?> localProperties = new();
        private readonly IBundledRequestVariableLoader _bundledRequestVariableLoader;

        public BundledRequestVariableResolver(IBundledRequestVariableLoader bundledRequestVariableLoader,
            IPropertyExpressionEvaluator propertyExpressionEvaluator, IPropertyEvaluator propertyEvaluator)
        {
            _bundledRequestVariableLoader = bundledRequestVariableLoader;
            _evaluator = propertyEvaluator;
            _expressionEvaluator = propertyExpressionEvaluator;
        }

        public IDictionary<string, VariableValue> LoadProperties()
        {
            foreach (var propertyValuePair in _bundledRequestVariableLoader.GetVariables())
            {
                if (!localProperties.ContainsKey(propertyValuePair.PropertyName))
                {
                    SetPropertyValue(propertyValuePair.PropertyName, propertyValuePair.PropertyValue);
                }
            }

            var dictionary = localProperties.Keys.AsParallel()
                .ToDictionary(x => x, GetPropertyValue);

            return dictionary;
        }

        public void SetPropertyValue(string property, VariableValue? value)
        {
            if (localProperties.TryAdd(property, value))
            {
                return;
            }

            localProperties[property] = value;
        }

        public void SetPropertyValue(string property, string value)
        {
            value ??= string.Empty;
            var variableValue = new VariableValue { Value = value, Type = value.GetType() };

            SetPropertyValue(property, variableValue);
        }

        public VariableValue? GetPropertyValue(string property)
        {
            VariableValue? propertyValue;
            if (localProperties.TryGetValue(property, out var localProperty))
            {
                propertyValue = EvaluatePropertyValue(localProperty);
            }
            else
            {
                return null;
            }

            var o = _expressionEvaluator.Evaluate(propertyValue.Value);
            return new VariableValue { Value = o, Type = o.GetType() };
        }

        public bool PropertiesLoaded()
        {
            return localProperties.Any();
        }

        public IDictionary<string, VariableValue> LocalProperties()
        {
            return localProperties.Keys.AsParallel()
                .ToDictionary(x => x, GetPropertyValue);
        }

        private VariableValue? EvaluatePropertyValue(VariableValue? value)
        {
            if (!(value.Value is string s)) return value;

            return _evaluator.Evaluate(this, s);
        }
    }
}

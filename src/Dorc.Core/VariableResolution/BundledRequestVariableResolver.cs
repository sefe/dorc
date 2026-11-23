using System.Collections.Concurrent;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.Core.VariableResolution
{
    public class BundledRequestVariableResolver : IVariableResolver
    {
        private readonly IPropertyEvaluator _evaluator;
        private readonly IPropertyExpressionEvaluator _expressionEvaluator;

        private readonly ConcurrentDictionary<string, VariableValue?> localProperties = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _pinnedProperties = new(StringComparer.OrdinalIgnoreCase);
        private readonly RequestSuppliedPropertyTracker _requestSuppliedProperties = new();
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
            foreach (var existingKey in localProperties.Keys.ToList())
            {
                if (!_pinnedProperties.ContainsKey(existingKey))
                {
                    localProperties.TryRemove(existingKey, out _);
                }
            }

            foreach (var propertyValuePair in _bundledRequestVariableLoader.GetVariables())
            {
                if (!_pinnedProperties.ContainsKey(propertyValuePair.PropertyName))
                {
                    SetUnpinnedPropertyValue(propertyValuePair.PropertyName, propertyValuePair.PropertyValue);
                }
            }

            var dictionary = localProperties.Keys.AsParallel()
                .ToDictionary(x => x, GetPropertyValue);

            return dictionary;
        }

        public void SetPropertyValue(string property, VariableValue? value)
        {
            _pinnedProperties[property] = 0;
            SetUnpinnedPropertyValue(property, value);
        }

        private void SetUnpinnedPropertyValue(string property, VariableValue? value)
        {
            if (localProperties.TryAdd(property, value))
            {
                return;
            }

            localProperties[property] = value;
        }

        public void SetPropertyValue(string property, string value)
        {
            _pinnedProperties[property] = 0;
            SetUnpinnedPropertyValue(property, value);
        }

        private void SetUnpinnedPropertyValue(string property, string value)
        {
            value ??= string.Empty;
            var variableValue = new VariableValue { Value = value, Type = value.GetType() };

            SetUnpinnedPropertyValue(property, variableValue);
        }

        public void SetRequestSuppliedPropertyValue(string property, string value)
        {
            _requestSuppliedProperties.Mark(property);
            SetPropertyValue(property, value);
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

            // As in VariableResolver: expression evaluation compiles and runs C#, so it is
            // applied only where no request-supplied text can reach the evaluated string.
            if (_requestSuppliedProperties.IsInfluenced(property, RawValueOf))
            {
                return propertyValue;
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

        private string? RawValueOf(string property)
        {
            return localProperties.TryGetValue(property, out var stored) && stored?.Value is string raw
                ? raw
                : null;
        }

        private VariableValue? EvaluatePropertyValue(VariableValue? value)
        {
            if (!(value.Value is string s)) return value;

            return _evaluator.Evaluate(this, s);
        }
    }
}

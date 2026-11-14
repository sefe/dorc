using System.Collections.Concurrent;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.PersistentData.Security;

namespace Dorc.Core.VariableResolution
{
    public class BundledRequestVariableResolver : IVariableResolver
    {
        private readonly IPropertyEvaluator _evaluator;
        private readonly IPropertyExpressionEvaluator _expressionEvaluator;

        private readonly ConcurrentDictionary<string, VariableValue?> localProperties = new();
        private readonly RequestSuppliedPropertyTracker _requestSuppliedProperties = new();
        private readonly IBundledRequestVariableLoader _bundledRequestVariableLoader;
        private readonly IScriptScopeConfigValues? _scriptScopeConfigValues;

        public BundledRequestVariableResolver(
            IBundledRequestVariableLoader bundledRequestVariableLoader,
            IPropertyExpressionEvaluator propertyExpressionEvaluator,
            IPropertyEvaluator propertyEvaluator)
            : this(bundledRequestVariableLoader, propertyExpressionEvaluator, propertyEvaluator, null)
        {
        }

        public BundledRequestVariableResolver(
            IBundledRequestVariableLoader bundledRequestVariableLoader,
            IPropertyExpressionEvaluator propertyExpressionEvaluator,
            IPropertyEvaluator propertyEvaluator,
            IScriptScopeConfigValues? scriptScopeConfigValues)
        {
            _bundledRequestVariableLoader = bundledRequestVariableLoader;
            _evaluator = propertyEvaluator;
            _expressionEvaluator = propertyExpressionEvaluator;
            _scriptScopeConfigValues = scriptScopeConfigValues;
        }

        private bool IsWithheld(string? property) =>
            _scriptScopeConfigValues != null && _scriptScopeConfigValues.IsWithheld(property);

        public IDictionary<string, VariableValue> LoadProperties()
        {
            foreach (var propertyValuePair in _bundledRequestVariableLoader.GetVariables())
            {
                if (IsWithheld(propertyValuePair.PropertyName))
                {
                    continue;
                }

                if (!localProperties.ContainsKey(propertyValuePair.PropertyName))
                {
                    SetPropertyValue(propertyValuePair.PropertyName, propertyValuePair.PropertyValue);
                }
            }

            var dictionary = localProperties.Keys.AsParallel()
                .Where(x => !IsWithheld(x))
                .Select(x => new { Key = x, Value = GetPropertyValue(x) })
                .Where(x => x.Value != null)
                .ToDictionary(x => x.Key, x => x.Value!);

            return dictionary;
        }

        public void SetPropertyValue(string property, VariableValue? value)
        {
            if (IsWithheld(property))
            {
                return;
            }

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

        public void SetRequestSuppliedPropertyValue(string property, string value)
        {
            if (IsWithheld(property))
            {
                return;
            }

            _requestSuppliedProperties.Mark(property);
            SetPropertyValue(property, value);
        }

        public VariableValue? GetPropertyValue(string property)
        {
            if (IsWithheld(property))
            {
                return null;
            }

            VariableValue? propertyValue;
            if (localProperties.TryGetValue(property, out var localProperty))
            {
                propertyValue = EvaluatePropertyValue(localProperty);
            }
            else
            {
                return null;
            }

            if (propertyValue?.Value == null)
            {
                return propertyValue;
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
                .Where(x => !IsWithheld(x))
                .Select(x => new { Key = x, Value = GetPropertyValue(x) })
                .Where(x => x.Value != null)
                .ToDictionary(x => x.Key, x => x.Value!);
        }

        private string? RawValueOf(string property)
        {
            return localProperties.TryGetValue(property, out var stored) && stored?.Value is string raw
                ? raw
                : null;
        }

        private VariableValue? EvaluatePropertyValue(VariableValue? value)
        {
            if (!(value?.Value is string s)) return value;

            return _evaluator.Evaluate(this, s);
        }
    }
}

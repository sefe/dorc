using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.ExceptionServices;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.VariableResolution
{
    public class VariableResolver : IVariableResolver
    {
        private readonly PropertyExpressionEvaluator _expressionEvaluator;

        private readonly ConcurrentDictionary<string, VariableValue?> localProperties = new();

        private readonly RequestSuppliedPropertyTracker _requestSuppliedProperties = new();
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IPropertyEvaluator _propertyEvaluator;
        private readonly ILogger _logger;
        private IScriptScopeConfigValues? _scriptScopeConfigValues;

        public VariableResolver(
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            ILoggerFactory loggerFactory,
            IPropertyEvaluator propertyEvaluator)
            : this(propertyValuesPersistentSource, loggerFactory, propertyEvaluator, null)
        {
        }

        public VariableResolver(
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            ILoggerFactory loggerFactory,
            IPropertyEvaluator propertyEvaluator,
            IScriptScopeConfigValues? scriptScopeConfigValues)
        {
            _propertyEvaluator = propertyEvaluator;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _scriptScopeConfigValues = scriptScopeConfigValues;

            _expressionEvaluator = new PropertyExpressionEvaluator(loggerFactory.CreateLogger<PropertyExpressionEvaluator>());
            _logger = loggerFactory.CreateLogger<VariableResolver>();
        }

        public void ExcludeWithheldKeys(IScriptScopeConfigValues? scriptScopeConfigValues)
        {
            _scriptScopeConfigValues = scriptScopeConfigValues;
            if (scriptScopeConfigValues == null)
            {
                return;
            }

            foreach (var key in localProperties.Keys.Where(scriptScopeConfigValues.IsWithheld))
            {
                localProperties.TryRemove(key, out _);
            }
        }

        private bool IsWithheld(string? property) =>
            _scriptScopeConfigValues != null && _scriptScopeConfigValues.IsWithheld(property);

        public bool PropertiesLoaded()
        {
            return localProperties.Any();
        }

        public IDictionary<string, VariableValue> LocalProperties()
        {
            try
            {
                return localProperties.Keys.AsParallel()
                    .Where(x => !IsWithheld(x))
                    .ToDictionary(x => x, GetPropertyValue);
            }
            catch (AggregateException ex) when (ex.Flatten().InnerExceptions.OfType<PropertyExpressionEvaluationException>().FirstOrDefault() is { } evaluationException)
            {
                ExceptionDispatchInfo.Capture(evaluationException).Throw();
                throw;
            }
        }

        public IDictionary<string, VariableValue> LoadProperties()
        {
            foreach (var propertyValuePair in _propertyValuesPersistentSource.LoadAllPropertiesIntoCache())
            {
                if (IsWithheld(propertyValuePair.Key))
                {
                    continue;
                }

                if (!localProperties.ContainsKey(propertyValuePair.Key))
                {
                    SetPropertyValue(propertyValuePair.Key, propertyValuePair.Value.Value);
                }
            }

            try
            {
                var dictionary = localProperties.Keys.AsParallel()
                    .Where(x => !IsWithheld(x))
                    .Select(x => new { Key = x, Value = GetPropertyValue(x) })
                    .Where(x => x.Value != null)
                    .ToDictionary(x => x.Key, x => x.Value!);

                return dictionary;
            }
            catch (AggregateException ex) when (ex.Flatten().InnerExceptions.OfType<PropertyExpressionEvaluationException>().FirstOrDefault() is { } evaluationException)
            {
                ExceptionDispatchInfo.Capture(evaluationException).Throw();
                throw;
            }
        }

        public VariableValue? GetPropertyValue(string property)
        {
            if (IsWithheld(property))
            {
                return null;
            }

            try
            {
                VariableValue? propertyValue;
                if (localProperties.ContainsKey(property))
                {
                    if (_propertyValuesPersistentSource.IsCachedPropertySecure(property))
                        return localProperties[property];

                    propertyValue = EvaluatePropertyValue(localProperties[property]);
                }
                else
                {
                    var repositoryValue = _propertyValuesPersistentSource.GetCachedPropertyValue(property);
                    if (repositoryValue == null)
                        return null;

                    var variableValue = new VariableValue { Value = repositoryValue.Value, Type = repositoryValue.Value.GetType() };
                    if (repositoryValue.Property.Secure)
                        return variableValue;

                    propertyValue = EvaluatePropertyValue(variableValue);
                }

                // Expression evaluation is applied only to values whose content is
                // administrator-curated. The grammar can no longer run code, but a
                // request-supplied value, or a curated value that interpolates one, is still
                // input the requester chose, and it is kept out of the evaluator on principle.
                if (_requestSuppliedProperties.IsInfluenced(property, RawValueOf))
                {
                    return propertyValue;
                }

                var o = _expressionEvaluator.Evaluate(propertyValue.Value);
                return new VariableValue { Value = o, Type = o.GetType() };
            }
            catch (PropertyExpressionEvaluationException)
            {
                _logger.LogError(
                    "Failed to evaluate property expression for property '{Property}'.",
                    LogText.SingleLine(property));
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate property '{Property}': {ErrorMessage}", property, ex.Message);
                return null;
            }
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

            // The mark is applied before the value is stored, so a concurrent reader can see
            // the property marked but not yet set, never set but not yet marked.
            _requestSuppliedProperties.Mark(property);
            SetPropertyValue(property, value);
        }

        /// <summary>
        /// The stored, uninterpolated string value of a property, or null when it holds no
        /// string or is not present. Supplied to the tracker so reference-following happens
        /// on raw values, before any interpolation or evaluation.
        /// </summary>
        private string? RawValueOf(string property)
        {
            return localProperties.TryGetValue(property, out var stored) && stored?.Value is string raw
                ? raw
                : null;
        }

        private VariableValue? EvaluatePropertyValue(VariableValue? value)
        {
            if (!(value.Value is string s)) return value;

            return _propertyEvaluator.Evaluate(this, s);
        }
    }
}
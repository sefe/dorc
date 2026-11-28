using System.Collections.Concurrent;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.VariableResolution
{
    public class VariableResolver : IVariableResolver
    {
        private readonly PropertyExpressionEvaluator _expressionEvaluator;

        private readonly ConcurrentDictionary<string, VariableValue?> localProperties = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _arrayProperties = new(StringComparer.OrdinalIgnoreCase);
        private readonly ThreadLocal<HashSet<string>> _activeResolutionPath =
            new(() => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        private readonly RequestSuppliedPropertyTracker _requestSuppliedProperties = new();
        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IPropertyEvaluator _propertyEvaluator;
        private readonly ILogger _logger;

        public VariableResolver(IPropertyValuesPersistentSource propertyValuesPersistentSource, ILoggerFactory loggerFactory, IPropertyEvaluator propertyEvaluator)
        {
            _propertyEvaluator = propertyEvaluator;
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            
            _expressionEvaluator = new PropertyExpressionEvaluator(loggerFactory.CreateLogger<PropertyExpressionEvaluator>());
            _logger = loggerFactory.CreateLogger<VariableResolver>();
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

        public IDictionary<string, VariableValue> LoadProperties()
        {
            foreach (var propertyValuePair in _propertyValuesPersistentSource.LoadAllPropertiesIntoCache())
            {
                if (propertyValuePair.Value.Property is { IsArray: true })
                {
                    _arrayProperties[propertyValuePair.Key] = true;
                }

                if (!localProperties.ContainsKey(propertyValuePair.Key))
                {
                    SetPropertyValue(propertyValuePair.Key, propertyValuePair.Value.Value);
                }
            }

            var dictionary = localProperties.Keys.AsParallel()
                .Select(x => new { Key = x, Value = GetPropertyValue(x) })
                .Where(x => x.Value != null)
                .ToDictionary(x => x.Key, x => x.Value!);

            return dictionary;
        }

        public VariableValue? GetPropertyValue(string property)
        {
            if (string.IsNullOrEmpty(property))
            {
                return null;
            }

            var resolutionPath = _activeResolutionPath.Value!;
            if (!resolutionPath.Add(property))
            {
                _logger.LogError("Detected circular property reference during variable evaluation.");
                return null;
            }

            try
            {
                VariableValue? propertyValue;
                if (localProperties.ContainsKey(property))
                {
                    var isSecureLocal = _propertyValuesPersistentSource.IsCachedPropertySecure(property);
                    if (IsArrayProperty(property))
                    {
                        return ResolveArrayPropertyValue(property, localProperties[property], isSecureLocal);
                    }

                    if (isSecureLocal)
                        return localProperties[property];

                    propertyValue = EvaluatePropertyValue(localProperties[property]);
                }
                else
                {
                    var repositoryValue = _propertyValuesPersistentSource.GetCachedPropertyValue(property);
                    if (repositoryValue == null)
                        return null;

                    var variableValue = new VariableValue { Value = repositoryValue.Value, Type = repositoryValue.Value.GetType() };
                    var isSecureRepo = repositoryValue.Property?.Secure ?? false;
                    if (repositoryValue.Property is { IsArray: true } || IsArrayProperty(property))
                    {
                        return ResolveArrayPropertyValue(property, variableValue, isSecureRepo);
                    }

                    if (isSecureRepo)
                        return variableValue;

                    propertyValue = EvaluatePropertyValue(variableValue);
                }

                // Expression evaluation compiles and runs C#. It is applied only to values
                // whose content is administrator-curated: a request-supplied value, or a
                // curated value that interpolates one, would otherwise let anyone able to
                // submit a deployment request execute code inside the Monitor process.
                if (_requestSuppliedProperties.IsInfluenced(property, RawValueOf))
                {
                    return propertyValue;
                }

                var o = _expressionEvaluator.Evaluate(propertyValue.Value);
                return new VariableValue { Value = o, Type = o.GetType() };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate property '{Property}': {ErrorMessage}", property, ex.Message);
                return null;
            }
            finally
            {
                resolutionPath.Remove(property);
            }
        }

        private bool IsArrayProperty(string property)
        {
            return _arrayProperties.ContainsKey(property)
                || _propertyValuesPersistentSource.IsCachedPropertyArray(property);
        }

        private VariableValue ResolveArrayPropertyValue(string property, VariableValue rawVariableValue, bool isSecure)
        {
            if (rawVariableValue.Value is string[] existingStringArray)
            {
                return new VariableValue { Value = existingStringArray, Type = typeof(string[]) };
            }

            var rawString = Convert.ToString(rawVariableValue.Value)!;
            if (TryParseJsonStringArray(rawString, out var parsedElements))
            {
                if (isSecure)
                {
                    return new VariableValue { Value = parsedElements, Type = typeof(string[]) };
                }

                var resolvedElements = new List<string>(parsedElements.Length);
                var isInfluenced = _requestSuppliedProperties.IsInfluenced(property, RawValueOf);
                foreach (var element in parsedElements)
                {
                    var evaluatedElement = EvaluatePropertyValue(new VariableValue { Value = element, Type = typeof(string) })!;
                    var elementObject = !isInfluenced
                        ? _expressionEvaluator.Evaluate(evaluatedElement.Value)
                        : evaluatedElement.Value;

                    if (elementObject is string[] nestedStrings)
                    {
                        resolvedElements.AddRange(nestedStrings);
                    }
                    else
                    {
                        resolvedElements.Add(Convert.ToString(elementObject)!);
                    }
                }

                var resolvedArray = resolvedElements.ToArray();
                return new VariableValue { Value = resolvedArray, Type = typeof(string[]) };
            }

            if (isSecure)
            {
                return new VariableValue { Value = new[] { rawString }, Type = typeof(string[]) };
            }

            var evaluated = EvaluatePropertyValue(rawVariableValue)!;
            var evaluatedObject = !_requestSuppliedProperties.IsInfluenced(property, RawValueOf)
                ? _expressionEvaluator.Evaluate(evaluated.Value)
                : evaluated.Value;

            if (evaluatedObject is string[] resolvedStringArray)
            {
                return new VariableValue { Value = resolvedStringArray, Type = typeof(string[]) };
            }

            var singleElement = new[] { Convert.ToString(evaluatedObject)! };
            return new VariableValue { Value = singleElement, Type = typeof(string[]) };
        }

        private static bool TryParseJsonStringArray(string value, out string[] elements)
        {
            elements = Array.Empty<string>();
            if (!value.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                elements = JsonSerializer.Deserialize<string[]>(value)!;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
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

        public void SetRequestSuppliedPropertyValue(string property, string value)
        {
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
            if (localProperties.TryGetValue(property, out var stored) && stored?.Value is string raw)
            {
                return raw;
            }

            return _propertyValuesPersistentSource.GetCachedPropertyValue(property)?.Value;
        }

        private VariableValue? EvaluatePropertyValue(VariableValue? value)
        {
            if (!(value.Value is string s)) return value;

            return _propertyEvaluator.Evaluate(this, s);
        }
    }
}
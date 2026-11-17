using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Core.VariableResolution
{
    public class PropertyResolutionTracer : IPropertyResolutionTracer
    {
        private const int MaxRecursionDepth = 32;
        private const int MaxRequestedProperties = 250;
        private const string ExpressionMarker = "fn:";

        private static readonly HashSet<string> RuntimeOnlyProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            PropertyValueScopeOptionsFixed.DropFolder,
            PropertyValueScopeOptionsFixed.ScriptRoot,
            PropertyValueScopeOptionsFixed.DeploymentLogDir,
            PropertyValueScopeOptionsFixed.BuildNumber,
            PropertyValueScopeOptionsFixed.RefDataApiUrl,
            PropertyValueScopeOptionsFixed.EnvOwner
        };

        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IVariableScopeOptionsResolver _variableScopeOptionsResolver;
        private readonly PropertyParser _parser = new();

        public PropertyResolutionTracer(
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IVariableScopeOptionsResolver variableScopeOptionsResolver)
        {
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _variableScopeOptionsResolver = variableScopeOptionsResolver;
        }

        public IReadOnlyList<PropertyResolutionTraceApiModel> TraceProperties(
            PropertyResolutionPreviewRequestApiModel request,
            EnvironmentApiModel? environment,
            ClaimsPrincipal user)
        {
            if (request.PropertyNames.Count == 0)
            {
                return Array.Empty<PropertyResolutionTraceApiModel>();
            }

            var uniqueNames = request.PropertyNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRequestedProperties)
                .ToList();

            var snapshot = BuildResolutionSnapshot(request, environment);
            var results = new List<PropertyResolutionTraceApiModel>(uniqueNames.Count);

            foreach (var propName in uniqueNames)
            {
                var activeStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var chain = new List<string>();
                var evaluated = EvaluateProperty(propName, snapshot, activeStack, chain, 0);
                results.Add(ToTraceApiModel(propName, evaluated, chain));
            }

            return results;
        }

        private ResolutionSnapshot BuildResolutionSnapshot(
            PropertyResolutionPreviewRequestApiModel request,
            EnvironmentApiModel? environment)
        {
            var entries = new Dictionary<string, CandidateEntry>(StringComparer.OrdinalIgnoreCase);
            var requestSuppliedTracker = new RequestSuppliedPropertyTracker();
            var envName = environment?.EnvironmentName;
            var envSecure = environment?.EnvironmentSecure ?? false;

            if (!envSecure)
            {
                var globalProps = _propertyValuesPersistentSource.GetGlobalProperties();
                foreach (var prop in globalProps)
                {
                    var name = prop.Property.Name;
                    var isSecure = prop.Property.Secure;
                    var isArray = prop.Property.IsArray;
                    var rawVal = isSecure ? RequestPropertyRedaction.Marker : prop.Value;

                    entries[name] = new CandidateEntry(
                        rawVal,
                        rawVal,
                        isArray ? "Array" : "String",
                        "GlobalDefault",
                        "Default",
                        isSecure,
                        !string.IsNullOrEmpty(envName));
                }
            }

            if (!string.IsNullOrEmpty(envName))
            {
                var envProps = _propertyValuesPersistentSource.GetEnvironmentProperties(envName, null);
                foreach (var prop in envProps)
                {
                    var name = prop.Property.Name;
                    var filterScope = prop.PropertyValueFilter;
                    var isAncestor = !string.IsNullOrEmpty(filterScope) &&
                                     !string.Equals(filterScope, envName, StringComparison.OrdinalIgnoreCase);
                    var isSecure = prop.Property.Secure;
                    var isArray = prop.Property.IsArray;
                    var rawVal = isSecure ? RequestPropertyRedaction.Marker : prop.Value;
                    var resolvedScope = string.IsNullOrEmpty(filterScope) ? envName : filterScope;

                    entries[name] = new CandidateEntry(
                        rawVal,
                        rawVal,
                        isArray ? "Array" : "String",
                        isAncestor ? "AncestorEnvironmentScope" : "EnvironmentScope",
                        resolvedScope,
                        isSecure,
                        false);
                }

                entries[PropertyValueScopeOptionsFixed.EnvironmentName] = new CandidateEntry(
                    envName,
                    envName,
                    "String",
                    "BuiltInScopeOption",
                    envName,
                    false,
                    false);

                var isProdString = environment!.EnvironmentIsProd.ToString();
                entries["IsProd"] = new CandidateEntry(
                    isProdString,
                    isProdString,
                    "String",
                    "BuiltInScopeOption",
                    envName,
                    false,
                    false);

                var scopeCollector = new ScopeOptionsCollector();
                try
                {
                    _variableScopeOptionsResolver.SetPropertyValues(scopeCollector, environment);
                }
                catch (InvalidOperationException)
                {
                    // Tag collision or missing details should not abort preview of other variables
                }

                foreach (var kvp in scopeCollector.Collected)
                {
                    var valObj = kvp.Value.Value;
                    var valType = GetFriendlyTypeName(kvp.Value.Type);
                    var strRepr = valObj is string s ? s : JsonSerializer.Serialize(valObj);

                    entries[kvp.Key] = new CandidateEntry(
                        strRepr,
                        valObj,
                        valType,
                        "BuiltInScopeOption",
                        envName,
                        false,
                        false);
                }
            }

            if (request.RequestOverrides != null)
            {
                foreach (var ov in request.RequestOverrides)
                {
                    if (string.IsNullOrWhiteSpace(ov.PropertyName))
                    {
                        continue;
                    }

                    var name = ov.PropertyName.Trim();
                    requestSuppliedTracker.Mark(name);
                    var existingSecure = entries.TryGetValue(name, out var prev) && prev.IsSecure;
                    var isSecure = ov.IsSensitive || existingSecure;
                    var raw = isSecure ? RequestPropertyRedaction.Marker : ov.PropertyValue;

                    entries[name] = new CandidateEntry(
                        raw,
                        raw,
                        "String",
                        "RequestOverride",
                        "RequestOverride",
                        isSecure,
                        false);
                }
            }

            return new ResolutionSnapshot(entries, requestSuppliedTracker);
        }

        private EvaluatedResult EvaluateProperty(
            string propertyName,
            ResolutionSnapshot snapshot,
            HashSet<string> activeStack,
            List<string> chain,
            int depth)
        {
            if (!snapshot.Entries.TryGetValue(propertyName, out var entry))
            {
                if (RuntimeOnlyProperties.Contains(propertyName))
                {
                    chain.Add($"${propertyName}$ -> [RuntimeOnly]");
                    return new EvaluatedResult(
                        "RuntimeOnly",
                        "MonitorRuntime",
                        null,
                        null,
                        "String",
                        false,
                        "RuntimeOnly",
                        null);
                }

                chain.Add($"${propertyName}$ -> [Unresolved]");
                return new EvaluatedResult(
                    "Unresolved",
                    null,
                    null,
                    null,
                    "String",
                    false,
                    "MissingInScope",
                    null);
            }

            if (depth > MaxRecursionDepth || !activeStack.Add(propertyName))
            {
                chain.Add($"${propertyName}$ -> [CircularReference]");
                return new EvaluatedResult(
                    entry.Source,
                    entry.ScopeName,
                    entry.RawValue,
                    null,
                    "String",
                    entry.IsSecure,
                    "CircularReference",
                    null);
            }

            try
            {
                if (entry.IsSecure)
                {
                    chain.Add($"${propertyName}$ ({entry.Source}) -> {RequestPropertyRedaction.Marker}");
                    return new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        RequestPropertyRedaction.Marker,
                        JsonSerializer.Serialize(RequestPropertyRedaction.Marker),
                        entry.ValueType,
                        true,
                        "Ok",
                        RequestPropertyRedaction.Marker);
                }

                if (entry.TypedValue is not string rawString)
                {
                    var serialized = JsonSerializer.Serialize(entry.TypedValue);
                    chain.Add($"${propertyName}$ ({entry.Source}) -> {entry.ValueType}");
                    return new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        entry.RawValue,
                        serialized,
                        entry.ValueType,
                        false,
                        "Ok",
                        entry.TypedValue);
                }

                List<Token> tokens;
                try
                {
                    tokens = _parser.Parse(rawString).ToList();
                }
                catch (InvalidOperationException)
                {
                    chain.Add($"${propertyName}$ ({entry.Source}) -> [UnclosedToken]");
                    return new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        JsonSerializer.Serialize(rawString),
                        entry.ValueType,
                        false,
                        "UnclosedToken",
                        rawString);
                }

                if (!tokens.Any(t => t is PropertyToken))
                {
                    var unescaped = string.Concat(tokens.Select(t => t.Value));
                    var diagnostic = ClassifyExpressionOrFallback(propertyName, unescaped, entry, snapshot);
                    chain.Add($"${propertyName}$ ({entry.Source}) -> {FormatPreviewSnippet(unescaped)}");

                    return new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        JsonSerializer.Serialize(unescaped),
                        entry.ValueType,
                        false,
                        diagnostic,
                        unescaped);
                }

                var evaluatedParts = new List<(Token Token, EvaluatedResult? ChildResult)>(tokens.Count);
                var anyChildSecure = false;
                var anyChildCircular = false;
                var anyChildMissing = false;
                var anyNonStringInConcat = false;
                string? childSpecialDiagnostic = null;

                foreach (var token in tokens)
                {
                    if (token is PropertyToken propToken)
                    {
                        var childResult = EvaluateProperty(
                            propToken.Value,
                            snapshot,
                            activeStack,
                            chain,
                            depth + 1);

                        evaluatedParts.Add((token, childResult));

                        if (childResult.IsSecure)
                        {
                            anyChildSecure = true;
                        }

                        if (childResult.Diagnostic == "CircularReference")
                        {
                            anyChildCircular = true;
                        }
                        else if (childResult.Source is "Unresolved" or "RuntimeOnly")
                        {
                            anyChildMissing = true;
                            childSpecialDiagnostic ??= childResult.Diagnostic;
                        }
                        else if (childResult.Diagnostic is "ExpressionDeferred" or "ExpressionBlockedByOverride")
                        {
                            childSpecialDiagnostic ??= childResult.Diagnostic;
                        }

                        if (tokens.Count > 1 && childResult.ValueType != "String")
                        {
                            anyNonStringInConcat = true;
                        }
                    }
                    else
                    {
                        evaluatedParts.Add((token, null));
                    }
                }

                if (anyChildSecure)
                {
                    chain.Add($"${propertyName}$ ({entry.Source}) -> {RequestPropertyRedaction.Marker} (transitive secret)");
                    return new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        RequestPropertyRedaction.Marker,
                        JsonSerializer.Serialize(RequestPropertyRedaction.Marker),
                        "String",
                        true,
                        "Ok",
                        RequestPropertyRedaction.Marker);
                }

                if (anyChildCircular)
                {
                    return new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        null,
                        "String",
                        false,
                        "CircularReference",
                        null);
                }

                if (anyChildMissing)
                {
                    chain.Add($"${propertyName}$ ({entry.Source}) -> {FormatPreviewSnippet(rawString)} (unresolved dependency)");
                    return new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        JsonSerializer.Serialize(rawString),
                        "String",
                        false,
                        childSpecialDiagnostic!,
                        rawString);
                }

                if (evaluatedParts.Count == 1)
                {
                    var singleChild = evaluatedParts[0].ChildResult!;
                    var diag = singleChild.Diagnostic;
                    if (diag == "Ok" && singleChild.RawResolvedObject is string singleValStr)
                    {
                        diag = ClassifyExpressionOrFallback(propertyName, singleValStr, entry, snapshot);
                    }

                    chain.Add($"${propertyName}$ ({entry.Source}) -> ${evaluatedParts[0].Token.Value}$");
                    return new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        singleChild.ResolvedValueJson,
                        singleChild.ValueType,
                        false,
                        diag,
                        singleChild.RawResolvedObject);
                }

                var sb = new StringBuilder();
                foreach (var (token, child) in evaluatedParts)
                {
                    sb.Append(child != null ? child.RawResolvedObject : token.Value);
                }

                var finalStr = sb.ToString();
                var finalDiag = childSpecialDiagnostic
                                ?? (anyNonStringInConcat
                                    ? "NonStringInConcatenation"
                                    : ClassifyExpressionOrFallback(propertyName, finalStr, entry, snapshot));

                chain.Add($"${propertyName}$ ({entry.Source}) -> {FormatPreviewSnippet(finalStr)}");
                return new EvaluatedResult(
                    entry.Source,
                    entry.ScopeName,
                    rawString,
                    JsonSerializer.Serialize(finalStr),
                    "String",
                    false,
                    finalDiag,
                    finalStr);
            }
            finally
            {
                activeStack.Remove(propertyName);
            }
        }

        private static string ClassifyExpressionOrFallback(
            string propertyName,
            string resolvedString,
            CandidateEntry entry,
            ResolutionSnapshot snapshot)
        {
            if (resolvedString.StartsWith(ExpressionMarker, StringComparison.Ordinal))
            {
                var isInfluenced = snapshot.RequestSuppliedTracker.IsInfluenced(
                    propertyName,
                    name => snapshot.Entries[name].RawValue);
                return isInfluenced ? "ExpressionBlockedByOverride" : "ExpressionDeferred";
            }

            return entry.IsFromFallbackDefault ? "FallbackToDefault" : "Ok";
        }

        private static PropertyResolutionTraceApiModel ToTraceApiModel(
            string propertyName,
            EvaluatedResult evaluated,
            List<string> chain)
        {
            return new PropertyResolutionTraceApiModel
            {
                PropertyName = propertyName,
                RawValue = evaluated.RawValue,
                ResolvedValueJson = evaluated.ResolvedValueJson,
                ValueType = evaluated.ValueType,
                Source = evaluated.Source,
                ScopeName = evaluated.ScopeName,
                ResolutionChain = chain,
                IsSecure = evaluated.IsSecure,
                Diagnostic = evaluated.Diagnostic
            };
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == typeof(string))
            {
                return "String";
            }

            if (type.IsArray)
            {
                return $"{type.GetElementType()!.Name}[]";
            }

            return type.Name;
        }

        private static string FormatPreviewSnippet(string value) =>
            value.Length <= 80 ? value : value.Substring(0, 77) + "...";

        private sealed record CandidateEntry(
            string? RawValue,
            object? TypedValue,
            string ValueType,
            string Source,
            string? ScopeName,
            bool IsSecure,
            bool IsFromFallbackDefault);

        private sealed record EvaluatedResult(
            string Source,
            string? ScopeName,
            string? RawValue,
            string? ResolvedValueJson,
            string ValueType,
            bool IsSecure,
            string Diagnostic,
            object? RawResolvedObject);

        private sealed record ResolutionSnapshot(
            Dictionary<string, CandidateEntry> Entries,
            RequestSuppliedPropertyTracker RequestSuppliedTracker);

        private sealed class ScopeOptionsCollector : IVariableResolver
        {
            public Dictionary<string, VariableValue> Collected { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public bool PropertiesLoaded() => true;

            public IDictionary<string, VariableValue> LocalProperties() => Collected;

            public IDictionary<string, VariableValue> LoadProperties() => Collected;

            public VariableValue? GetPropertyValue(string property) => null;

            public void SetPropertyValue(string property, VariableValue? value)
            {
                Collected[property] = value!;
            }

            public void SetPropertyValue(string property, string value)
            {
                Collected[property] = new VariableValue { Value = value, Type = typeof(string) };
            }

            public void SetRequestSuppliedPropertyValue(string property, string value) =>
                SetPropertyValue(property, value);
        }
    }
}

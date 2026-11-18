using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;

namespace Dorc.Core.VariableResolution
{
    public class PropertyResolutionTracer : IPropertyResolutionTracer
    {
        private const int MaxRecursionDepth = 32;
        private const int MaxRequestedProperties = 250;
        private const int MaxChainHops = 64;
        private const int MaxExpandedValueLength = 65_536;
        private const string ExpressionMarker = "fn:";

        private static readonly HashSet<string> RuntimeOnlyProperties = new(StringComparer.Ordinal)
        {
            PropertyValueScopeOptionsFixed.DropFolder,
            PropertyValueScopeOptionsFixed.ScriptRoot,
            PropertyValueScopeOptionsFixed.DeploymentLogDir,
            PropertyValueScopeOptionsFixed.BuildNumber,
            PropertyValueScopeOptionsFixed.RefDataApiUrl
        };

        private readonly IPropertyValuesPersistentSource _propertyValuesPersistentSource;
        private readonly IVariableScopeOptionsResolver _variableScopeOptionsResolver;
        private readonly IConfigValuesPersistentSource? _configValuesPersistentSource;
        private readonly ISecurityPrivilegesChecker? _securityPrivilegesChecker;
        private readonly IPropertyEncryptor? _propertyEncryptor;
        private readonly PropertyParser _parser = new();
        private readonly SafeExpressionPreviewEvaluator _expressionPreviewEvaluator = new();

        public PropertyResolutionTracer(
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IVariableScopeOptionsResolver variableScopeOptionsResolver,
            IConfigValuesPersistentSource? configValuesPersistentSource,
            ISecurityPrivilegesChecker? securityPrivilegesChecker,
            IPropertyEncryptor? propertyEncryptor)
        {
            _propertyValuesPersistentSource = propertyValuesPersistentSource;
            _variableScopeOptionsResolver = variableScopeOptionsResolver;
            _configValuesPersistentSource = configValuesPersistentSource;
            _securityPrivilegesChecker = securityPrivilegesChecker;
            _propertyEncryptor = propertyEncryptor;
        }

        public PropertyResolutionTracer(
            IPropertyValuesPersistentSource propertyValuesPersistentSource,
            IVariableScopeOptionsResolver variableScopeOptionsResolver)
            : this(propertyValuesPersistentSource, variableScopeOptionsResolver, null, null, null)
        {
        }

        public IReadOnlyList<PropertyResolutionTraceApiModel> TraceProperties(
            PropertyResolutionPreviewRequestApiModel request,
            EnvironmentApiModel? environment,
            ClaimsPrincipal user)
        {
            if (request == null || request.PropertyNames == null || request.PropertyNames.Count == 0)
            {
                return Array.Empty<PropertyResolutionTraceApiModel>();
            }

            var uniqueNames = request.PropertyNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(MaxRequestedProperties)
                .ToList();

            var snapshot = BuildResolutionSnapshot(request, environment, user);
            var evaluatedCache = new Dictionary<string, (EvaluatedResult Result, IReadOnlyList<string> Chain)>(StringComparer.Ordinal);
            var results = new List<PropertyResolutionTraceApiModel>(uniqueNames.Count);

            foreach (var propName in uniqueNames)
            {
                if (propName == "IsProd" &&
                    environment != null &&
                    !snapshot.Entries.ContainsKey("IsProd"))
                {
                    var isProdStr = environment.EnvironmentIsProd.ToString();
                    results.Add(new PropertyResolutionTraceApiModel
                    {
                        PropertyName = "IsProd",
                        RawValue = isProdStr,
                        ResolvedValueJson = JsonSerializer.Serialize(isProdStr),
                        ValueType = "String",
                        Source = "BuiltInScopeOption",
                        ScopeName = environment.EnvironmentName,
                        ResolutionChain = new List<string> { $"$IsProd$ (BuiltInScopeOption:{environment.EnvironmentName}) -> \"{isProdStr}\"" },
                        IsSecure = false,
                        Diagnostic = "Ok"
                    });
                    continue;
                }

                var activeStack = new HashSet<string>(StringComparer.Ordinal);
                var chain = new List<string>();
                var evaluated = EvaluateProperty(propName, snapshot, activeStack, evaluatedCache, chain, 0);
                results.Add(ToTraceApiModel(propName, evaluated, chain));
            }

            return results;
        }

        private ResolutionSnapshot BuildResolutionSnapshot(
            PropertyResolutionPreviewRequestApiModel request,
            EnvironmentApiModel? environment,
            ClaimsPrincipal user)
        {
            var entries = new Dictionary<string, CandidateEntry>(StringComparer.Ordinal);
            var requestSuppliedTracker = new RequestSuppliedPropertyTracker();
            var envName = environment?.EnvironmentName;
            var envSecure = environment?.EnvironmentSecure ?? false;
            var canReadTargetSecrets = !string.IsNullOrEmpty(envName) &&
                                       (_securityPrivilegesChecker?.CanReadSecrets(user, envName) ?? false);

            if (!envSecure)
            {
                var globalProps = _propertyValuesPersistentSource.GetGlobalProperties();
                foreach (var prop in globalProps)
                {
                    var name = prop.Property.Name;
                    var isSecure = prop.Property.Secure;
                    var isArray = prop.Property.IsArray;
                    var isRedacted = isSecure && (!canReadTargetSecrets || _propertyEncryptor == null);
                    var rawVal = ResolveStoredPropertyString(
                        prop.Value,
                        isSecure,
                        isArray,
                        isRedacted,
                        _propertyEncryptor);

                    entries[name] = new CandidateEntry(
                        rawVal,
                        rawVal,
                        isArray ? "Array" : "String",
                        "GlobalDefault",
                        "Default",
                        isSecure,
                        isRedacted,
                        !string.IsNullOrEmpty(envName));
                }
            }

            if (!string.IsNullOrEmpty(envName))
            {
                var sourceSecretsCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var envProps = _propertyValuesPersistentSource.GetEnvironmentProperties(envName, null);
                foreach (var prop in envProps)
                {
                    var name = prop.Property.Name;
                    var filterScope = prop.PropertyValueFilter;
                    var isAncestor = !string.IsNullOrEmpty(filterScope) &&
                                     !string.Equals(filterScope, envName, StringComparison.OrdinalIgnoreCase);
                    var isSecure = prop.Property.Secure;
                    var isArray = prop.Property.IsArray;
                    var resolvedScope = string.IsNullOrEmpty(filterScope) ? envName : filterScope;

                    var canReadSourceSecrets = false;
                    if (isSecure && canReadTargetSecrets)
                    {
                        if (!isAncestor)
                        {
                            canReadSourceSecrets = true;
                        }
                        else if (!sourceSecretsCache.TryGetValue(resolvedScope, out canReadSourceSecrets))
                        {
                            canReadSourceSecrets = _securityPrivilegesChecker!.CanReadSecrets(user, resolvedScope);
                            sourceSecretsCache[resolvedScope] = canReadSourceSecrets;
                        }
                    }

                    var isRedacted = isSecure && (!canReadTargetSecrets || !canReadSourceSecrets || _propertyEncryptor == null);
                    var rawVal = ResolveStoredPropertyString(
                        prop.Value,
                        isSecure,
                        isArray,
                        isRedacted,
                        _propertyEncryptor);

                    entries[name] = new CandidateEntry(
                        rawVal,
                        rawVal,
                        isArray ? "Array" : "String",
                        isAncestor ? "AncestorEnvironmentScope" : "EnvironmentScope",
                        resolvedScope,
                        isSecure,
                        isRedacted,
                        false);
                }

                if (_configValuesPersistentSource != null)
                {
                    var envIsProd = environment!.EnvironmentIsProd;
                    foreach (var configValue in _configValuesPersistentSource
                                 .GetAllConfigValues(false)
                                 .Where(cv => !entries.ContainsKey(cv.Key) &&
                                              (!cv.IsForProd.HasValue || cv.IsForProd.Value == envIsProd)))
                    {
                        var isSecure = configValue.Secure;
                        var rawVal = isSecure ? RequestPropertyRedaction.Marker : configValue.Value;
                        entries[configValue.Key] = new CandidateEntry(
                            rawVal,
                            rawVal,
                            "String",
                            "ConfigValue",
                            envName,
                            isSecure,
                            isSecure,
                            false);
                    }
                }

                entries[PropertyValueScopeOptionsFixed.EnvironmentName] = new CandidateEntry(
                    envName,
                    envName,
                    "String",
                    "BuiltInScopeOption",
                    envName,
                    false,
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
                    if (kvp.Key == PropertyValueScopeOptionsFixed.EnvOwnerEmails &&
                        valObj is string[] ownerEmailArray)
                    {
                        valObj = string.Join(";", ownerEmailArray);
                        valType = "String";
                    }

                    var strRepr = valObj is string s ? s : JsonSerializer.Serialize(valObj);

                    entries[kvp.Key] = new CandidateEntry(
                        strRepr,
                        valObj,
                        valType,
                        "BuiltInScopeOption",
                        envName,
                        false,
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
            Dictionary<string, (EvaluatedResult Result, IReadOnlyList<string> Chain)> evaluatedCache,
            List<string> chain,
            int depth)
        {
            if (evaluatedCache.TryGetValue(propertyName, out var cached))
            {
                AppendChainBounded(chain, cached.Chain);
                return cached.Result;
            }

            var hasFoundEntry = snapshot.Entries.TryGetValue(propertyName, out var foundEntry);
            var isSeededAtRuntimeWithoutOverride =
                RuntimeOnlyProperties.Contains(propertyName) &&
                (!hasFoundEntry || foundEntry!.Source is not ("BuiltInScopeOption" or "RequestOverride"));

            if (!hasFoundEntry || isSeededAtRuntimeWithoutOverride)
            {
                if (RuntimeOnlyProperties.Contains(propertyName))
                {
                    var runtimeHop = $"${propertyName}$ -> [RuntimeOnly]";
                    chain.Add(runtimeHop);
                    var runtimeRes = new EvaluatedResult(
                        "RuntimeOnly",
                        "MonitorRuntime",
                        null,
                        null,
                        "String",
                        false,
                        false,
                        "RuntimeOnly",
                        null);
                    evaluatedCache[propertyName] = (runtimeRes, new[] { runtimeHop });
                    return runtimeRes;
                }

                var missingHop = $"${propertyName}$ -> [Unresolved]";
                chain.Add(missingHop);
                var missingRes = new EvaluatedResult(
                    "Unresolved",
                    null,
                    null,
                    null,
                    "String",
                    false,
                    false,
                    "MissingInScope",
                    null);
                evaluatedCache[propertyName] = (missingRes, new[] { missingHop });
                return missingRes;
            }

            var entry = foundEntry!;

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
                    entry.IsRedactedSecret,
                    "CircularReference",
                    null);
            }

            var localChain = new List<string>();
            try
            {
                if (entry.IsRedactedSecret)
                {
                    localChain.Add($"${propertyName}$ ({entry.Source}) -> {RequestPropertyRedaction.Marker}");
                    var redactedRes = new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        RequestPropertyRedaction.Marker,
                        JsonSerializer.Serialize(RequestPropertyRedaction.Marker),
                        entry.ValueType,
                        true,
                        true,
                        "Ok",
                        RequestPropertyRedaction.Marker);
                    evaluatedCache[propertyName] = (redactedRes, localChain);
                    chain.AddRange(localChain);
                    return redactedRes;
                }

                if (entry.IsSecure || entry.TypedValue is not string rawString)
                {
                    var serialized = JsonSerializer.Serialize(entry.TypedValue);
                    localChain.Add($"${propertyName}$ ({entry.Source}) -> {entry.ValueType}");
                    var typedRes = new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        entry.RawValue,
                        serialized,
                        entry.ValueType,
                        entry.IsSecure,
                        false,
                        "Ok",
                        entry.TypedValue);
                    evaluatedCache[propertyName] = (typedRes, localChain);
                    chain.AddRange(localChain);
                    return typedRes;
                }

                List<Token> tokens;
                try
                {
                    tokens = _parser.Parse(rawString).ToList();
                }
                catch (InvalidOperationException)
                {
                    localChain.Add($"${propertyName}$ ({entry.Source}) -> [UnclosedToken]");
                    var unclosedRes = new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        JsonSerializer.Serialize(rawString),
                        entry.ValueType,
                        entry.IsSecure,
                        false,
                        "UnclosedToken",
                        rawString);
                    evaluatedCache[propertyName] = (unclosedRes, localChain);
                    chain.AddRange(localChain);
                    return unclosedRes;
                }

                if (!tokens.Any(t => t is PropertyToken))
                {
                    var unescaped = string.Concat(tokens.Select(t => t.Value));
                    var literalRes = EvaluateCandidateValue(
                        propertyName,
                        rawString,
                        unescaped,
                        entry.ValueType,
                        entry.IsSecure,
                        entry,
                        snapshot,
                        localChain);
                    evaluatedCache[propertyName] = (literalRes, localChain);
                    chain.AddRange(localChain);
                    return literalRes;
                }

                var evaluatedParts = new List<(Token Token, EvaluatedResult? ChildResult)>(tokens.Count);
                var anyChildRedactedSecret = false;
                var anyChildSecure = entry.IsSecure;
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
                            evaluatedCache,
                            localChain,
                            depth + 1);

                        evaluatedParts.Add((token, childResult));

                        if (childResult.IsRedactedSecret)
                        {
                            anyChildRedactedSecret = true;
                        }

                        if (childResult.IsSecure)
                        {
                            anyChildSecure = true;
                        }

                        if (childResult.Diagnostic == "CircularReference")
                        {
                            anyChildCircular = true;
                        }
                        else if (childResult.RawResolvedObject == null)
                        {
                            anyChildMissing = true;
                            childSpecialDiagnostic ??= childResult.Diagnostic;
                        }
                        else if (childResult.Diagnostic is not ("Ok" or "FallbackToDefault"))
                        {
                            childSpecialDiagnostic ??= childResult.Diagnostic;
                        }

                        if (tokens.Count > 1 && childResult.RawResolvedObject is not (string or ValueType))
                        {
                            anyNonStringInConcat = true;
                        }
                    }
                    else
                    {
                        evaluatedParts.Add((token, null));
                    }
                }

                if (anyChildRedactedSecret)
                {
                    localChain.Add($"${propertyName}$ ({entry.Source}) -> {RequestPropertyRedaction.Marker} (transitive secret)");
                    var transitiveSecretRes = new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        RequestPropertyRedaction.Marker,
                        JsonSerializer.Serialize(RequestPropertyRedaction.Marker),
                        "String",
                        true,
                        true,
                        "Ok",
                        RequestPropertyRedaction.Marker);
                    evaluatedCache[propertyName] = (transitiveSecretRes, localChain);
                    AppendChainBounded(chain, localChain);
                    return transitiveSecretRes;
                }

                if (anyChildCircular)
                {
                    AppendChainBounded(chain, localChain);
                    return new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        null,
                        "String",
                        anyChildSecure,
                        false,
                        "CircularReference",
                        null);
                }

                if (anyChildMissing)
                {
                    localChain.Add($"${propertyName}$ ({entry.Source}) -> {FormatPreviewSnippet(rawString)} (unresolved dependency)");
                    var missingChildRes = new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        JsonSerializer.Serialize(rawString),
                        "String",
                        anyChildSecure,
                        false,
                        childSpecialDiagnostic!,
                        rawString);
                    evaluatedCache[propertyName] = (missingChildRes, localChain);
                    AppendChainBounded(chain, localChain);
                    return missingChildRes;
                }

                if (evaluatedParts.Count == 1)
                {
                    var singleChild = evaluatedParts[0].ChildResult!;
                    var diag = singleChild.Diagnostic;
                    if (diag == "Ok" && singleChild.RawResolvedObject is string singleValStr)
                    {
                        diag = ClassifyExpressionOrFallback(propertyName, singleValStr, entry, snapshot);
                    }
                    else if (diag == "Ok" && entry.IsFromFallbackDefault)
                    {
                        diag = "FallbackToDefault";
                    }

                    localChain.Add($"${propertyName}$ ({entry.Source}) -> ${evaluatedParts[0].Token.Value}$");
                    var singleRes = new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        singleChild.ResolvedValueJson,
                        singleChild.ValueType,
                        anyChildSecure,
                        false,
                        diag,
                        singleChild.RawResolvedObject);
                    evaluatedCache[propertyName] = (singleRes, localChain);
                    AppendChainBounded(chain, localChain);
                    return singleRes;
                }

                var sb = new StringBuilder();
                var valueTruncated = false;
                foreach (var (token, child) in evaluatedParts)
                {
                    var chunkText = child != null ? $"{child.RawResolvedObject}" : token.Value;
                    var remaining = MaxExpandedValueLength - sb.Length;
                    if (chunkText.Length > remaining)
                    {
                        sb.Append(chunkText[..remaining]);
                        valueTruncated = true;
                    }
                    else
                    {
                        sb.Append(chunkText);
                    }
                }

                var finalStr = sb.ToString();
                if (childSpecialDiagnostic != null || anyNonStringInConcat || valueTruncated)
                {
                    var finalDiag = childSpecialDiagnostic
                                    ?? (anyNonStringInConcat ? "NonStringInConcatenation" : "ValueTruncated");

                    localChain.Add($"${propertyName}$ ({entry.Source}) -> {FormatPreviewSnippet(finalStr)}");
                    var specialConcatRes = new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        JsonSerializer.Serialize(finalStr),
                        "String",
                        anyChildSecure,
                        false,
                        finalDiag,
                        finalStr);
                    evaluatedCache[propertyName] = (specialConcatRes, localChain);
                    AppendChainBounded(chain, localChain);
                    return specialConcatRes;
                }

                var concatRes = EvaluateCandidateValue(
                    propertyName,
                    rawString,
                    finalStr,
                    "String",
                    anyChildSecure,
                    entry,
                    snapshot,
                    localChain);
                evaluatedCache[propertyName] = (concatRes, localChain);
                AppendChainBounded(chain, localChain);
                return concatRes;
            }
            finally
            {
                activeStack.Remove(propertyName);
            }
        }

        private static void AppendChainBounded(List<string> destination, IReadOnlyList<string> source)
        {
            for (var i = 0; i < source.Count && destination.Count < MaxChainHops; i++)
            {
                destination.Add(source[i]);
            }
        }

        private static string ResolveStoredPropertyString(
            string storedValue,
            bool isSecure,
            bool isArray,
            bool isRedacted,
            IPropertyEncryptor? encryptor)
        {
            if (isRedacted)
            {
                return RequestPropertyRedaction.Marker;
            }

            if (!isSecure)
            {
                return storedValue;
            }

            try
            {
                if (!isArray)
                {
                    return encryptor!.DecryptValue(storedValue)!;
                }

                if (JsonSerializer.Deserialize<string[]>(storedValue) is not { } items)
                {
                    return storedValue;
                }

                var decrypted = items.Select(encryptor!.DecryptValue).ToList();
                return JsonSerializer.Serialize(decrypted);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or System.Security.Cryptography.CryptographicException)
            {
                return RequestPropertyRedaction.Marker;
            }
        }

        private EvaluatedResult EvaluateCandidateValue(
            string propertyName,
            string rawString,
            string candidateString,
            string defaultValueType,
            bool isSecure,
            CandidateEntry entry,
            ResolutionSnapshot snapshot,
            List<string> localChain)
        {
            if (candidateString.StartsWith(ExpressionMarker, StringComparison.Ordinal))
            {
                var isInfluenced = snapshot.RequestSuppliedTracker.IsInfluenced(
                    propertyName,
                    name => snapshot.Entries.TryGetValue(name, out var candidate) && !candidate.IsSecure
                        ? candidate.RawValue
                        : string.Empty);

                if (isInfluenced)
                {
                    localChain.Add($"${propertyName}$ ({entry.Source}) -> {FormatPreviewSnippet(candidateString)}");
                    return new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        JsonSerializer.Serialize(candidateString),
                        defaultValueType,
                        isSecure,
                        false,
                        "ExpressionBlockedByOverride",
                        candidateString);
                }

                if (_expressionPreviewEvaluator.TryEvaluate(candidateString, out var evaluatedValue) &&
                    evaluatedValue != null)
                {
                    var evaluatedType = GetFriendlyTypeName(evaluatedValue.GetType());
                    var evaluatedJson = JsonSerializer.Serialize(evaluatedValue);
                    var evaluatedDisplay = evaluatedValue is string evaluatedStr ? evaluatedStr : evaluatedJson;
                    var evaluatedDiag = entry.IsFromFallbackDefault ? "FallbackToDefault" : "Ok";
                    localChain.Add(
                        $"${propertyName}$ ({entry.Source}) -> {FormatPreviewSnippet(candidateString)} => {FormatPreviewSnippet(evaluatedDisplay)}");
                    return new EvaluatedResult(
                        entry.Source,
                        entry.ScopeName,
                        rawString,
                        evaluatedJson,
                        evaluatedType,
                        isSecure,
                        false,
                        evaluatedDiag,
                        evaluatedValue);
                }

                localChain.Add($"${propertyName}$ ({entry.Source}) -> {FormatPreviewSnippet(candidateString)}");
                return new EvaluatedResult(
                    entry.Source,
                    entry.ScopeName,
                    rawString,
                    JsonSerializer.Serialize(candidateString),
                    defaultValueType,
                    isSecure,
                    false,
                    "ExpressionDeferred",
                    candidateString);
            }

            var diagnostic = entry.IsFromFallbackDefault ? "FallbackToDefault" : "Ok";
            localChain.Add($"${propertyName}$ ({entry.Source}) -> {FormatPreviewSnippet(candidateString)}");
            return new EvaluatedResult(
                entry.Source,
                entry.ScopeName,
                rawString,
                JsonSerializer.Serialize(candidateString),
                defaultValueType,
                isSecure,
                false,
                diagnostic,
                candidateString);
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
                    name => snapshot.Entries.TryGetValue(name, out var candidate) && !candidate.IsSecure
                        ? candidate.RawValue
                        : string.Empty);
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
            bool IsRedactedSecret,
            bool IsFromFallbackDefault);

        private sealed record EvaluatedResult(
            string Source,
            string? ScopeName,
            string? RawValue,
            string? ResolvedValueJson,
            string ValueType,
            bool IsSecure,
            bool IsRedactedSecret,
            string Diagnostic,
            object? RawResolvedObject);

        private sealed record ResolutionSnapshot(
            Dictionary<string, CandidateEntry> Entries,
            RequestSuppliedPropertyTracker RequestSuppliedTracker);

        private sealed class ScopeOptionsCollector : IVariableResolver
        {
            public Dictionary<string, VariableValue> Collected { get; } =
                new(StringComparer.Ordinal);

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

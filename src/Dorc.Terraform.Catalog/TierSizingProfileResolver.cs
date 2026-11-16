using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Dorc.Terraform.Catalog
{
    /// <summary>
    /// Resolves tier-weighted sizing and topology defaults across stock Terraform
    /// catalog modules (`postgresql-database`, `clickhouse-database`, `kafka-topic`,
    /// `cosmosdb`, `storage-account`, `service-bus`, `redis-cache`, `key-vault`)
    /// while enforcing ephemeral safety invariants.
    /// </summary>
    public static class TierSizingProfileResolver
    {
        private static readonly Regex AlphanumericOnly = new("[^a-z0-9]+", RegexOptions.Compiled);
        private static readonly Regex DatabaseIdentifierChars = new("[^a-z0-9_]+", RegexOptions.Compiled);
        private static readonly Regex HyphenSlugChars = new("[^a-z0-9-]+", RegexOptions.Compiled);
        private static readonly Regex RepeatedHyphens = new("-{2,}", RegexOptions.Compiled);
        private static readonly Regex RepeatedUnderscores = new("_{2,}", RegexOptions.Compiled);

        private static readonly IReadOnlyDictionary<string, string> TierParameterRules =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["create_service"] = CatalogInputSuggestions.Hints.CreateService,
                ["create_resource_group"] = CatalogInputSuggestions.Hints.CreateResourceGroup,
                ["partitions"] = CatalogInputSuggestions.Hints.Partitions,
                ["retention_ms"] = CatalogInputSuggestions.Hints.RetentionMs,
                ["throughput"] = CatalogInputSuggestions.Hints.Throughput,
                ["plan"] = CatalogInputSuggestions.Hints.Plan,
                ["service_name"] = CatalogInputSuggestions.Hints.ServiceName,
                ["database_name"] = CatalogInputSuggestions.Hints.DatabaseName,
                ["sku"] = CatalogInputSuggestions.Hints.Sku,
                ["sku_name"] = CatalogInputSuggestions.Hints.Sku,
                ["storage_account_name"] = CatalogInputSuggestions.Hints.ResourceName,
                ["key_vault_name"] = CatalogInputSuggestions.Hints.ResourceName,
                ["vault_name"] = CatalogInputSuggestions.Hints.ResourceName,
                ["namespace_name"] = CatalogInputSuggestions.Hints.ResourceName,
                ["servicebus_namespace_name"] = CatalogInputSuggestions.Hints.ResourceName,
                ["cosmosdb_account_name"] = CatalogInputSuggestions.Hints.ResourceName,
                ["account_name"] = CatalogInputSuggestions.Hints.ResourceName,
                ["redis_name"] = CatalogInputSuggestions.Hints.ResourceName,
                ["cache_name"] = CatalogInputSuggestions.Hints.ResourceName,
                ["resource_name"] = CatalogInputSuggestions.Hints.ResourceName,
            };

        private static readonly IReadOnlyDictionary<string, Func<TerraformTemplateParameter, CatalogInputSuggestions.Context, CatalogInputSuggestions.Suggestion?>> TierRuleEvaluators =
            new Dictionary<string, Func<TerraformTemplateParameter, CatalogInputSuggestions.Context, CatalogInputSuggestions.Suggestion?>>(StringComparer.Ordinal)
            {
                [CatalogInputSuggestions.Hints.CreateService] = static (_, context) => SuggestCreateService(context),
                [CatalogInputSuggestions.Hints.CreateResourceGroup] = static (_, context) => SuggestCreateResourceGroup(context),
                [CatalogInputSuggestions.Hints.Partitions] = static (_, context) => SuggestPartitions(context),
                [CatalogInputSuggestions.Hints.RetentionMs] = static (_, context) => SuggestRetentionMs(context),
                [CatalogInputSuggestions.Hints.Throughput] = static (_, context) => SuggestThroughput(context),
                [CatalogInputSuggestions.Hints.Plan] = static (_, context) => SuggestPlan(context),
                [CatalogInputSuggestions.Hints.ServiceName] = static (parameter, context) => SuggestServiceName(parameter, context),
                [CatalogInputSuggestions.Hints.DatabaseName] = static (_, context) => SuggestDatabaseName(context),
                [CatalogInputSuggestions.Hints.Sku] = static (parameter, context) => SuggestSku(parameter, context),
                [CatalogInputSuggestions.Hints.ResourceName] = static (parameter, context) => SuggestResourceName(parameter, context),
            };

        public static CatalogInputSuggestions.Suggestion? TrySuggestForParameter(
            TerraformTemplateParameter parameter,
            CatalogInputSuggestions.Context context)
        {
            var rule = parameter.Suggest;
            if (rule is null && !TierParameterRules.TryGetValue(parameter.Name, out rule))
            {
                return null;
            }

            return TrySuggestForRule(rule!, parameter, context);
        }

        public static CatalogInputSuggestions.Suggestion? TrySuggestForRule(
            string rule,
            TerraformTemplateParameter parameter,
            CatalogInputSuggestions.Context context) =>
            TierRuleEvaluators.TryGetValue(rule, out var evaluator) ? evaluator(parameter, context) : null;

        public static IReadOnlyDictionary<string, string?> ResolveTierWeightedInputs(
            TerraformTemplateManifest manifest,
            CatalogInputSuggestions.Context context,
            IReadOnlyDictionary<string, string?>? tierOverrides = null)
        {
            if (manifest is null) throw new ArgumentNullException(nameof(manifest));
            if (context is null) throw new ArgumentNullException(nameof(context));

            var effectiveContext = string.IsNullOrWhiteSpace(context.ModuleName)
                ? context with { ModuleName = manifest.Name }
                : context;

            var resolved = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var parameter in manifest.Parameters)
            {
                if (parameter.Sensitive)
                {
                    continue;
                }

                if (effectiveContext.IsEphemeral && IsTerminationProtectionParameter(parameter.Name))
                {
                    resolved[parameter.Name] = "false";
                    continue;
                }

                if (tierOverrides is not null
                    && (tierOverrides.TryGetValue($"{effectiveContext.ComponentName}:{parameter.Name}", out var overrideValue)
                        || tierOverrides.TryGetValue(parameter.Name, out overrideValue))
                    && !string.IsNullOrEmpty(overrideValue))
                {
                    resolved[parameter.Name] = overrideValue;
                    continue;
                }

                var suggestion = CatalogInputSuggestions.For(parameter, effectiveContext);
                if (suggestion is not null)
                {
                    resolved[parameter.Name] = suggestion.Value;
                }
                else if (parameter.Default is not null)
                {
                    resolved[parameter.Name] = parameter.Default;
                }
            }

            return resolved;
        }

        public static bool IsTerminationProtectionParameter(string parameterName) =>
            string.Equals(parameterName, "termination_protection", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameterName, "service_termination_protection", StringComparison.OrdinalIgnoreCase);

        public static CatalogInputSuggestions.Suggestion SuggestCreateService(CatalogInputSuggestions.Context context)
        {
            var tier = CatalogInputSuggestions.EnvironmentTier(context, out _);
            var isShared = IsLowTier(tier);
            return isShared
                ? new CatalogInputSuggestions.Suggestion(
                    "false",
                    $"Tier \"{tier}\" reuses the shared service from the parent environment.")
                : new CatalogInputSuggestions.Suggestion(
                    "true",
                    $"Tier \"{tier}\" provisions a dedicated service instance.");
        }

        public static CatalogInputSuggestions.Suggestion SuggestDatabaseName(CatalogInputSuggestions.Context context)
        {
            var dbName = FormatDatabaseName(ResolveApplicationKey(context), context.EnvironmentName);
            return new CatalogInputSuggestions.Suggestion(
                dbName,
                $"Normalized database name for application and environment \"{context.EnvironmentName}\".");
        }

        public static CatalogInputSuggestions.Suggestion SuggestPlan(CatalogInputSuggestions.Context context)
        {
            var tier = CatalogInputSuggestions.EnvironmentTier(context, out _);
            var isClickHouse = string.Equals(context.ModuleName, "clickhouse-database", StringComparison.OrdinalIgnoreCase);

            string plan;
            if (IsLowTier(tier) || tier is "qa" or "it" or "in" or "np")
            {
                plan = "startup-4";
            }
            else if (tier == "ut")
            {
                plan = "startup-16";
            }
            else
            {
                plan = isClickHouse ? "business-16" : "business-8";
            }

            return new CatalogInputSuggestions.Suggestion(
                plan,
                $"Tier-weighted plan \"{plan}\" for tier \"{tier}\".");
        }

        public static CatalogInputSuggestions.Suggestion? SuggestServiceName(
            TerraformTemplateParameter parameter,
            CatalogInputSuggestions.Context context)
        {
            if (string.IsNullOrWhiteSpace(context.ModuleName)
                || !context.ModuleName.Contains("kafka", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var tier = CatalogInputSuggestions.EnvironmentTier(context, out _);
            string serviceName;
            if (IsLowTier(tier))
            {
                serviceName = "traveler-unstable-dev";
            }
            else if (tier is "pr" or "dr")
            {
                var prodCandidates = new[] { "traveler-production", "traveler-prod" };
                serviceName = parameter.AllowedValues is { Count: > 0 }
                    ? SelectAllowedCandidate(parameter.AllowedValues, prodCandidates)
                    : prodCandidates[0];
            }
            else
            {
                serviceName = "traveler-non-prod";
            }

            return new CatalogInputSuggestions.Suggestion(
                serviceName,
                $"Tier-weighted service \"{serviceName}\" for tier \"{tier}\".");
        }

        public static CatalogInputSuggestions.Suggestion SuggestPartitions(CatalogInputSuggestions.Context context)
        {
            var tier = CatalogInputSuggestions.EnvironmentTier(context, out _);
            string partitions;
            if (IsLowTier(tier))
            {
                partitions = "1";
            }
            else if (tier is "qa" or "it" or "in" or "np" or "ut")
            {
                partitions = "3";
            }
            else
            {
                partitions = "6";
            }

            return new CatalogInputSuggestions.Suggestion(
                partitions,
                $"Tier-weighted partition count for tier \"{tier}\".");
        }

        public static CatalogInputSuggestions.Suggestion SuggestRetentionMs(CatalogInputSuggestions.Context context)
        {
            var tier = CatalogInputSuggestions.EnvironmentTier(context, out _);
            string retention;
            if (IsLowTier(tier))
            {
                retention = "3600000";
            }
            else if (tier is "qa" or "it" or "in" or "np" or "ut")
            {
                retention = "86400000";
            }
            else
            {
                retention = "604800000";
            }

            return new CatalogInputSuggestions.Suggestion(
                retention,
                $"Tier-weighted retention (ms) for tier \"{tier}\".");
        }

        public static CatalogInputSuggestions.Suggestion SuggestThroughput(CatalogInputSuggestions.Context context)
        {
            var tier = CatalogInputSuggestions.EnvironmentTier(context, out _);
            string throughput;
            if (IsLowTier(tier))
            {
                throughput = "400";
            }
            else if (tier is "qa" or "it" or "in" or "np" or "ut")
            {
                throughput = "1000";
            }
            else
            {
                throughput = "4000";
            }

            return new CatalogInputSuggestions.Suggestion(
                throughput,
                $"Tier-weighted throughput for tier \"{tier}\".");
        }

        public static CatalogInputSuggestions.Suggestion SuggestSku(
            TerraformTemplateParameter parameter,
            CatalogInputSuggestions.Context context)
        {
            var tier = CatalogInputSuggestions.EnvironmentTier(context, out _);
            string[] candidates;
            if (IsLowTier(tier))
            {
                var prefersStandard = string.Equals(context.ModuleName, "key-vault", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(context.ModuleName, "storage-account", StringComparison.OrdinalIgnoreCase);
                candidates = prefersStandard
                    ? new[] { "Standard", "Basic" }
                    : new[] { "Basic", "Standard" };
            }
            else if (tier is "qa" or "it" or "in" or "np" or "ut")
            {
                candidates = new[] { "Standard", "Basic", "Premium" };
            }
            else
            {
                candidates = new[] { "Premium", "Standard" };
            }

            var preferredSku = candidates[0];
            string chosen;
            if (parameter.AllowedValues is { Count: > 0 })
            {
                chosen = SelectAllowedCandidate(parameter.AllowedValues, candidates);
            }
            else
            {
                chosen = preferredSku;
            }

            return new CatalogInputSuggestions.Suggestion(
                chosen,
                $"Tier-weighted SKU \"{chosen}\" for tier \"{tier}\".");
        }

        public static CatalogInputSuggestions.Suggestion SuggestCreateResourceGroup(CatalogInputSuggestions.Context context)
        {
            var tier = CatalogInputSuggestions.EnvironmentTier(context, out _);
            var createRg = tier is "pp" or "ff" or "pr" or "dr";
            return new CatalogInputSuggestions.Suggestion(
                createRg ? "true" : "false",
                createRg
                    ? $"Tier \"{tier}\" provisions an isolated resource group."
                    : $"Tier \"{tier}\" reuses the existing shared resource group.");
        }

        public static CatalogInputSuggestions.Suggestion SuggestResourceName(
            TerraformTemplateParameter parameter,
            CatalogInputSuggestions.Context context)
        {
            var tierCode = CatalogInputSuggestions.ToTwoCharTierCode(
                CatalogInputSuggestions.EnvironmentTier(context, out _));
            var name = FormatResourceName(
                context.ModuleName,
                parameter.Name,
                ResolveApplicationKey(context),
                context.EnvironmentName,
                tierCode);
            return new CatalogInputSuggestions.Suggestion(
                name,
                $"Environment-suffixed resource name for \"{context.EnvironmentName}\".");
        }

        private static string? ResolveApplicationKey(CatalogInputSuggestions.Context context)
        {
            if (string.IsNullOrWhiteSpace(context.ComponentName))
            {
                return context.ApplicationCode;
            }

            return string.IsNullOrWhiteSpace(context.ApplicationCode)
                ? context.ComponentName
                : $"{context.ApplicationCode}-{context.ComponentName}";
        }

        public static string FormatDatabaseName(string? applicationCode, string environmentName)
        {
            var rawAppKey = string.IsNullOrWhiteSpace(applicationCode)
                ? "app"
                : applicationCode.Trim().ToLowerInvariant();
            var rawEnvKey = string.IsNullOrWhiteSpace(environmentName)
                ? string.Empty
                : environmentName.Trim().ToLowerInvariant();
            var app = NormalizeDatabaseToken(applicationCode, "app");
            var env = NormalizeDatabaseToken(environmentName, "env");
            var canonicalAppSlug = NormalizeHyphenSlug(applicationCode, "app").Replace('-', '_');
            var canonicalEnvSlug = NormalizeHyphenSlug(environmentName, "env");
            var hasLossyNormalization =
                !string.Equals(rawAppKey, canonicalAppSlug, StringComparison.Ordinal) ||
                !string.Equals(rawEnvKey, canonicalEnvSlug, StringComparison.Ordinal) ||
                EndsWithDelimitedHex6(env, '_');

            if (hasLossyNormalization)
            {
                env = $"{env}_{ComputeShortHash($"{rawAppKey}:{rawEnvKey}")}";
            }

            var candidate = $"{app}_{env}";
            if (candidate.Length > 63)
            {
                var hash6 = ComputeShortHash($"{rawAppKey}:{rawEnvKey}");
                var truncatedPrefix = CompressHeadTail(candidate, 56).TrimEnd('_');
                candidate = $"{truncatedPrefix}_{hash6}";
            }

            return candidate;
        }

        public static string FormatResourceName(
            string? moduleName,
            string parameterName,
            string? applicationCode,
            string environmentName,
            string? environmentTier = null)
        {
            var tier2 = ResolveResourceTierToken(environmentTier, environmentName);
            var rawAppKey = string.IsNullOrWhiteSpace(applicationCode)
                ? "app"
                : applicationCode.Trim().ToLowerInvariant();
            var rawEnvKey = string.IsNullOrWhiteSpace(environmentName)
                ? string.Empty
                : environmentName.Trim().ToLowerInvariant();
            var rawCompositeKey = $"{tier2}:{rawAppKey}:{rawEnvKey}";

            var isStorageAccount = string.Equals(moduleName, "storage-account", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameterName, "storage_account_name", StringComparison.OrdinalIgnoreCase);
            if (isStorageAccount)
            {
                var appAlnum = NormalizeAlphanumeric(applicationCode, "app");
                var envAlnum = NormalizeAlphanumeric(environmentName, "env");
                var hasLossyApp = appAlnum.Length > 8 || !string.Equals(rawAppKey, appAlnum, StringComparison.Ordinal);
                if (appAlnum.Length > 8) appAlnum = CompressHeadTail(appAlnum, 8);
                var maxEnvLen = 24 - 4 - appAlnum.Length;
                var hasLossyEnv = envAlnum.Length > maxEnvLen || !string.Equals(rawEnvKey, envAlnum, StringComparison.Ordinal) || EndsWithCompactHex6(envAlnum);
                if (hasLossyApp || hasLossyEnv)
                {
                    var hash6 = ComputeShortHash(rawCompositeKey);
                    var prefixSlice = CompressHeadTail(envAlnum, maxEnvLen - 6);
                    envAlnum = $"{prefixSlice}{hash6}";
                }
                return $"sa{tier2}{appAlnum}{envAlnum}";
            }

            var isKeyVault = string.Equals(moduleName, "key-vault", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameterName, "key_vault_name", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameterName, "vault_name", StringComparison.OrdinalIgnoreCase);
            if (isKeyVault)
            {
                var appAlnum = NormalizeAlphanumeric(applicationCode, "app");
                var envSlug = NormalizeHyphenSlug(environmentName, "env");
                var hasLossyApp = appAlnum.Length > 6 || !string.Equals(rawAppKey, appAlnum, StringComparison.Ordinal);
                if (appAlnum.Length > 6) appAlnum = CompressHeadTail(appAlnum, 6);
                var maxEnvLen = 24 - 8 - appAlnum.Length;
                var hasLossyEnv = envSlug.Length > maxEnvLen || !string.Equals(rawEnvKey, envSlug, StringComparison.Ordinal) || EndsWithDelimitedHex6(envSlug, '-');
                if (hasLossyApp || hasLossyEnv)
                {
                    var hash6 = ComputeShortHash(rawCompositeKey);
                    var envPrefix = CompressHeadTail(envSlug, maxEnvLen - 7).Trim('-');
                    envSlug = $"{envPrefix}-{hash6}";
                }
                return $"akv-{tier2}-{appAlnum}-{envSlug}";
            }

            var prefix = ResolveResourcePrefix(moduleName, parameterName);
            var appToken = NormalizeAlphanumeric(applicationCode, "app");
            var envToken = NormalizeHyphenSlug(environmentName, "env");
            var hasLossyAppToken = appToken.Length > 12 || !string.Equals(rawAppKey, appToken, StringComparison.Ordinal);
            if (appToken.Length > 12) appToken = CompressHeadTail(appToken, 12);
            var maxTotalLen = prefix == "cosno" ? 44 : 50;
            var maxEnvTokenLen = maxTotalLen - prefix.Length - tier2.Length - appToken.Length - 3;
            var hasLossyEnvToken = envToken.Length > maxEnvTokenLen || !string.Equals(rawEnvKey, envToken, StringComparison.Ordinal) || EndsWithDelimitedHex6(envToken, '-');
            if (hasLossyAppToken || hasLossyEnvToken)
            {
                var hash6 = ComputeShortHash(rawCompositeKey);
                var envPrefix = CompressHeadTail(envToken, maxEnvTokenLen - 7).Trim('-');
                envToken = $"{envPrefix}-{hash6}";
            }
            return $"{prefix}-{tier2}-{appToken}-{envToken}";
        }

        private static string ResolveResourceTierToken(string? explicitTier, string environmentName)
        {
            if (!string.IsNullOrWhiteSpace(explicitTier))
            {
                var tier = CatalogInputSuggestions.EnvironmentTier(
                    new CatalogInputSuggestions.Context(string.Empty, false, null, null, ExplicitTierOverride: explicitTier),
                    out var reason);
                if (reason.StartsWith("From explicit tier override", StringComparison.Ordinal))
                {
                    return CatalogInputSuggestions.ToTwoCharTierCode(tier);
                }
            }

            return CatalogInputSuggestions.ToTwoCharTierCode(
                CatalogInputSuggestions.EnvironmentTier(
                    new CatalogInputSuggestions.Context(environmentName, false, null, null),
                    out _));
        }

        private static string ResolveResourcePrefix(string? moduleName, string parameterName)
        {
            if (string.Equals(moduleName, "cosmosdb", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameterName, "cosmosdb_account_name", StringComparison.OrdinalIgnoreCase))
            {
                return "cosno";
            }

            if (string.Equals(moduleName, "service-bus", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameterName, "namespace_name", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameterName, "servicebus_namespace_name", StringComparison.OrdinalIgnoreCase))
            {
                return "sbn";
            }

            if (string.Equals(moduleName, "redis-cache", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameterName, "redis_name", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameterName, "cache_name", StringComparison.OrdinalIgnoreCase))
            {
                return "redis";
            }

            return "res";
        }

        private static bool IsLowTier(string tier) => tier is "ephemeral" or "xx" or "dv";

        private static string SelectAllowedCandidate(
            IReadOnlyList<string> allowedValues,
            IReadOnlyList<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                if (allowedValues.Contains(candidate, StringComparer.Ordinal))
                {
                    return candidate;
                }
            }

            return candidates[0];
        }

        internal static string ComputeShortHash(string value)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes, 0, 3).ToLowerInvariant();
        }

        private static string CompressHeadTail(string token, int maxLength)
        {
            if (token.Length <= maxLength)
            {
                return token;
            }

            var headLen = (maxLength + 1) / 2;
            var tailLen = maxLength - headLen;
            var head = token[..headLen].TrimEnd('-', '_');
            var tail = token[^tailLen..].TrimStart('-', '_');
            return string.Concat(head, tail);
        }

        private static bool EndsWithCompactHex6(string token)
            => token.Length > 6 && IsLowerHex6(token.AsSpan(token.Length - 6));

        internal static bool EndsWithDelimitedHex6(string token, char delimiter)
            => token.Length > 7 && token[^7] == delimiter && IsLowerHex6(token.AsSpan(token.Length - 6));

        private static bool IsLowerHex6(ReadOnlySpan<char> span)
        {
            for (var i = 0; i < 6; i++)
            {
                var c = span[i];
                if (!char.IsAsciiDigit(c) && (c < 'a' || c > 'f'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeAlphanumeric(string? raw, string fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var cleaned = AlphanumericOnly.Replace(raw.Trim().ToLowerInvariant(), string.Empty);
            return cleaned.Length == 0 ? fallback : cleaned;
        }

        private static string NormalizeDatabaseToken(string? raw, string fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var lowered = raw.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            var cleaned = DatabaseIdentifierChars.Replace(lowered, string.Empty);
            var collapsed = RepeatedUnderscores.Replace(cleaned, "_").Trim('_');
            return collapsed.Length == 0 ? fallback : collapsed;
        }

        private static string NormalizeHyphenSlug(string? raw, string fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var lowered = raw.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            var cleaned = HyphenSlugChars.Replace(lowered, string.Empty);
            var collapsed = RepeatedHyphens.Replace(cleaned, "-").Trim('-');
            return collapsed.Length == 0 ? fallback : collapsed;
        }
    }
}

using System.Text.RegularExpressions;

namespace Dorc.Terraform.Catalog
{
    // Derives non-secret starting values for catalog inputs from the project and
    // environment the wizard is targeting, following the organisation's Kafka
    // topic naming and cloud landing-zone standards. Suggestions are shown to
    // the operator as editable prefill; saved
    // configuration and environment properties always take precedence, and
    // nothing here is applied unless it is part of the submitted inputs.
    //
    // A manifest parameter may name its rule explicitly with `suggest:`; when
    // absent the parameter name selects the rule so existing immutable
    // manifests benefit without republishing.
    public static class CatalogInputSuggestions
    {
        public const string ProdCloudName = "custom-sefe-prod-workload-azure-uksouth";
        public const string NonProdCloudName = "custom-sefe-nprod-workload-azure-uksouth";
        public const string DefaultLocation = "uksouth";
        public const string DefaultBusinessVertical = "tr";

        public static class Hints
        {
            public const string DorcEnvironment = "dorc_environment";
            public const string EnvironmentTier = "environment_tier";
            public const string IsProd = "is_prod";
            public const string CloudByTier = "cloud_by_tier";
            public const string TerminationProtection = "termination_protection";
            public const string BusinessVertical = "business_vertical";
            public const string ApplicationCode = "application_code";
            public const string Scope = "scope";
            public const string Location = "location";
            public const string CreateService = "create_service";
            public const string CreateResourceGroup = "create_resource_group";
            public const string Partitions = "partitions";
            public const string RetentionMs = "retention_ms";
            public const string Throughput = "throughput";
            public const string Plan = "plan";
            public const string ServiceName = "service_name";
            public const string DatabaseName = "database_name";
            public const string Sku = "sku";
            public const string ResourceName = "resource_name";

            public static readonly IReadOnlyList<string> All = new[]
            {
                DorcEnvironment, EnvironmentTier, IsProd, CloudByTier, TerminationProtection,
                BusinessVertical, ApplicationCode, Scope, Location,
                CreateService, CreateResourceGroup, Partitions, RetentionMs, Throughput,
                Plan, ServiceName, DatabaseName, Sku, ResourceName,
            };
        }

        public sealed record Context(
            string EnvironmentName,
            bool EnvironmentIsProd,
            string? BusinessVertical,
            string? ApplicationCode,
            bool IsEphemeral = false,
            string? ExplicitTierOverride = null,
            string? ModuleName = null,
            string? ComponentName = null);

        public sealed record Suggestion(string Value, string Reason);

        private static readonly Regex NonSlugCharacters = new("[^a-z0-9_-]+", RegexOptions.Compiled);
        private static readonly Regex Tokens = new("[a-z]+|[0-9]+", RegexOptions.Compiled);

        // Group IT environment tiers plus the aliases operators actually use in
        // DOrc environment names (e.g. "APP PREPROD", "APP Prod", "APP QA 01").
        private static readonly IReadOnlyDictionary<string, string> TierAliases =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ephemeral"] = "ephemeral", ["eph"] = "ephemeral", ["temp"] = "ephemeral", ["temporary"] = "ephemeral",
                ["dv"] = "dv", ["dev"] = "dv", ["development"] = "dv",
                ["qa"] = "qa", ["test"] = "qa", ["systest"] = "qa", ["st"] = "qa",
                ["it"] = "it", ["int"] = "it", ["integration"] = "it",
                ["ut"] = "ut", ["uat"] = "ut",
                ["pp"] = "pp", ["preprod"] = "pp", ["pre"] = "pp", ["staging"] = "pp", ["stg"] = "pp",
                ["ff"] = "ff", ["fixonfail"] = "ff", ["fof"] = "ff",
                ["pr"] = "pr", ["prod"] = "pr", ["production"] = "pr", ["prd"] = "pr", ["live"] = "pr",
                ["dr"] = "dr",
            };

        private static readonly IReadOnlyDictionary<string, string> NameRules =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dorc_environment"] = Hints.DorcEnvironment,
                ["environment_tier"] = Hints.EnvironmentTier,
                ["env_tier"] = Hints.EnvironmentTier,
                ["is_prod"] = Hints.IsProd,
                ["is_production"] = Hints.IsProd,
                ["cloud_name"] = Hints.CloudByTier,
                ["termination_protection"] = Hints.TerminationProtection,
                ["service_termination_protection"] = Hints.TerminationProtection,
                ["business_vertical"] = Hints.BusinessVertical,
                ["origin"] = Hints.ApplicationCode,
                ["application_code"] = Hints.ApplicationCode,
                ["app_code"] = Hints.ApplicationCode,
                ["scope"] = Hints.Scope,
                ["location"] = Hints.Location,
            };

        private static readonly IReadOnlyDictionary<string, Func<TerraformTemplateParameter, Context, Suggestion?>> RuleEvaluators =
            new Dictionary<string, Func<TerraformTemplateParameter, Context, Suggestion?>>(StringComparer.Ordinal)
            {
                [Hints.DorcEnvironment] = static (_, context) =>
                {
                    var slug = FormatDorcEnvironmentSlug(context.EnvironmentName, context.IsEphemeral);
                    return slug.Length == 0 ? null : new Suggestion(slug, $"DOrc environment \"{context.EnvironmentName}\".");
                },
                [Hints.EnvironmentTier] = static (_, context) =>
                {
                    var tier = EnvironmentTier(context, out var reason);
                    return new Suggestion(tier, reason);
                },
                [Hints.IsProd] = static (_, context) =>
                {
                    var isProd = IsProduction(context);
                    return new Suggestion(isProd ? "true" : "false",
                        $"Environment \"{context.EnvironmentName}\" {(isProd ? "is" : "is not")} production.");
                },
                [Hints.CloudByTier] = static (_, context) =>
                    IsProduction(context)
                        ? new Suggestion(ProdCloudName, "Production workloads use the prod Aiven cloud.")
                        : new Suggestion(NonProdCloudName, "Non-production workloads use the nprod Aiven cloud."),
                [Hints.TerminationProtection] = static (_, context) =>
                {
                    if (context.IsEphemeral)
                    {
                        return new Suggestion("false", "Ephemeral environments always disable termination protection.");
                    }
                    var tier = EnvironmentTier(context, out string _);
                    return tier is "dv" or "ephemeral"
                        ? new Suggestion("false", "Development tier resources are expected to be rebuilt freely.")
                        : new Suggestion("true", $"Protect {tier.ToUpperInvariant()} tier resources from accidental destroy.");
                },
                [Hints.BusinessVertical] = static (_, context) =>
                    string.IsNullOrEmpty(context.BusinessVertical)
                        ? new Suggestion(DefaultBusinessVertical, "No business vertical on the project; platform default. Set it in Project Ref Data.")
                        : new Suggestion(context.BusinessVertical!, "Business vertical configured on the project."),
                [Hints.ApplicationCode] = static (_, context) =>
                    string.IsNullOrEmpty(context.ApplicationCode)
                        ? null
                        : new Suggestion(context.ApplicationCode!, "Application code configured on the project."),
                [Hints.Scope] = static (_, context) =>
                {
                    var tier = EnvironmentTier(context, out string _);
                    return tier is "dv" or "ephemeral"
                        ? new Suggestion("tst", "Development tier topics default to the test scope.")
                        : new Suggestion("gbl", "Global scope unless the topic is local or user-specific.");
                },
                [Hints.Location] = static (_, _) =>
                    new Suggestion(DefaultLocation, "Platform default Azure region."),
            };

        public static bool IsKnownHint(string? hint) =>
            hint is not null && Hints.All.Contains(hint, StringComparer.Ordinal);

        public static IReadOnlyDictionary<string, string?> ResolveTierWeightedInputs(
            TerraformTemplateManifest manifest,
            Context context,
            IReadOnlyDictionary<string, string?>? tierOverrides = null)
            => TierSizingProfileResolver.ResolveTierWeightedInputs(manifest, context, tierOverrides);

        /// <summary>
        /// Returns a suggestion for the parameter, or null when no rule applies,
        /// the parameter is sensitive, or the derived value would violate the
        /// manifest's own constraints (allowed values / pattern).
        /// </summary>
        public static Suggestion? For(TerraformTemplateParameter parameter, Context context)
        {
            if (parameter.Sensitive) return null;

            Suggestion? suggestion = null;
            var rule = parameter.Suggest;
            if (rule is not null)
            {
                suggestion = Apply(rule, parameter, context);
                if (suggestion is null && ShouldUseTierSizingSuggestions(context))
                {
                    suggestion = TierSizingProfileResolver.TrySuggestForParameter(parameter, context);
                }
            }
            else if (NameRules.TryGetValue(parameter.Name, out var generalRule))
            {
                suggestion = Apply(generalRule, parameter, context);
            }
            else if (ShouldUseTierSizingSuggestions(context))
            {
                suggestion = TierSizingProfileResolver.TrySuggestForParameter(parameter, context);
            }

            if (suggestion is null) return null;
            if (parameter.AllowedValues is { Count: > 0 } &&
                !parameter.AllowedValues.Contains(suggestion.Value, StringComparer.Ordinal))
                return null;
            if (!string.IsNullOrEmpty(parameter.Pattern))
            {
                try
                {
                    if (!Regex.IsMatch(suggestion.Value, parameter.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                        return null;
                }
                catch (ArgumentException) { return null; }
                catch (RegexMatchTimeoutException) { return null; }
            }
            return suggestion;
        }

        public static string NormaliseEnvironmentName(string environmentName)
        {
            // Mirrors the kafka-topic module: characters outside a-z 0-9 _ - are removed.
            return NonSlugCharacters.Replace(environmentName.Trim().ToLowerInvariant(), string.Empty);
        }

        public static string FormatDorcEnvironmentSlug(string environmentName, bool isEphemeral)
        {
            var slug = NormaliseEnvironmentName(environmentName);
            if (slug.Length == 0 || !isEphemeral)
            {
                return slug;
            }

            var rawEnvKey = environmentName.Trim().ToLowerInvariant();
            if (!string.Equals(slug, rawEnvKey, StringComparison.Ordinal) ||
                TierSizingProfileResolver.EndsWithDelimitedHex6(slug, '-'))
            {
                slug = $"{slug}-{TierSizingProfileResolver.ComputeShortHash(rawEnvKey)}";
            }

            return slug;
        }

        /// <summary>
        /// Environment tier derived from explicit tier overrides, tokens in the
        /// environment name (e.g. "APP QA 01" → qa, "APP PREPROD" → pp), or the
        /// environment's ephemeral/production flags when no token matches.
        /// </summary>
        public static string EnvironmentTier(Context context, out string reason)
        {
            if (!string.IsNullOrWhiteSpace(context.ExplicitTierOverride))
            {
                var overrideToken = context.ExplicitTierOverride.Trim().ToLowerInvariant();
                if (TierAliases.TryGetValue(overrideToken, out var overrideTier))
                {
                    if (overrideTier == "ephemeral" && context.EnvironmentIsProd)
                    {
                        reason = $"Explicit tier override \"{context.ExplicitTierOverride}\" cannot set ephemeral tier on production environment \"{context.EnvironmentName}\"; falling back to \"pr\".";
                        return "pr";
                    }
                    reason = $"From explicit tier override \"{context.ExplicitTierOverride}\" for environment \"{context.EnvironmentName}\".";
                    return overrideTier;
                }
            }

            foreach (Match token in Tokens.Matches(context.EnvironmentName.ToLowerInvariant()))
            {
                if (TierAliases.TryGetValue(token.Value, out var tier))
                {
                    if (tier == "ephemeral" && context.EnvironmentIsProd)
                    {
                        continue;
                    }
                    if (context.IsEphemeral && !context.EnvironmentIsProd && tier is "pr" or "dr")
                    {
                        continue;
                    }
                    reason = $"From \"{token.Value.ToUpperInvariant()}\" in environment name \"{context.EnvironmentName}\".";
                    return tier;
                }
            }

            if (context.IsEphemeral && !context.EnvironmentIsProd)
            {
                reason = $"Environment \"{context.EnvironmentName}\" is flagged ephemeral.";
                return "ephemeral";
            }

            reason = context.EnvironmentIsProd
                ? $"Environment \"{context.EnvironmentName}\" is flagged production."
                : $"Environment \"{context.EnvironmentName}\" is not flagged production.";
            return context.EnvironmentIsProd ? "pr" : "dv";
        }

        private static bool ShouldUseTierSizingSuggestions(Context context) =>
            context.IsEphemeral
            || !string.IsNullOrWhiteSpace(context.ModuleName)
            || !string.IsNullOrWhiteSpace(context.ExplicitTierOverride);

        private static bool IsProduction(Context context)
        {
            var tier = EnvironmentTier(context, out _);
            return context.EnvironmentIsProd || tier == "pr" || tier == "dr";
        }

        private static Suggestion? Apply(string rule, TerraformTemplateParameter parameter, Context context) =>
            RuleEvaluators.TryGetValue(rule, out var evaluator) ? evaluator(parameter, context) : null;
    }
}

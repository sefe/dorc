using System.Text.RegularExpressions;

namespace Dorc.Terraform.Catalog
{
    // Derives non-secret starting values for catalog inputs from the project and
    // environment the wizard is targeting, following the SEFE naming standards
    // (Messaging Standards - Kafka - Topic Naming; Cloud Application Landing
    // Zones). Suggestions are shown to the operator as editable prefill; saved
    // configuration and environment properties always take precedence, and
    // nothing here is applied unless it is part of the submitted inputs.
    //
    // A manifest parameter may name its rule explicitly with `suggest:`; when
    // absent the parameter name selects the rule so existing immutable
    // manifests benefit without republishing.
    public static class CatalogInputSuggestions
    {
        public const string StandardsPageUrl = "https://wiki/pages/viewpage.action?pageId=516177153";
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

            public static readonly IReadOnlyList<string> All = new[]
            {
                DorcEnvironment, EnvironmentTier, IsProd, CloudByTier, TerminationProtection,
                BusinessVertical, ApplicationCode, Scope, Location,
            };
        }

        public sealed record Context(
            string EnvironmentName,
            bool EnvironmentIsProd,
            string? BusinessVertical,
            string? ApplicationCode);

        public sealed record Suggestion(string Value, string Reason);

        private static readonly Regex NonSlugCharacters = new("[^a-z0-9_-]+", RegexOptions.Compiled);
        private static readonly Regex Tokens = new("[a-z]+|[0-9]+", RegexOptions.Compiled);

        // Group IT environment tiers plus the aliases operators actually use in
        // DOrc environment names (e.g. "LTRM PREPROD", "RVC Prod", "GMA QA 01").
        private static readonly IReadOnlyDictionary<string, string> TierAliases =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dv"] = "dv", ["dev"] = "dv", ["development"] = "dv",
                ["qa"] = "qa", ["test"] = "qa", ["systest"] = "qa", ["st"] = "qa",
                ["it"] = "it", ["int"] = "it", ["integration"] = "it",
                ["ut"] = "ut", ["uat"] = "ut",
                ["pp"] = "pp", ["preprod"] = "pp", ["pre"] = "pp", ["staging"] = "pp", ["stg"] = "pp",
                ["ff"] = "ff",
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

        public static bool IsKnownHint(string? hint) =>
            hint is not null && Hints.All.Contains(hint, StringComparer.Ordinal);

        /// <summary>
        /// Returns a suggestion for the parameter, or null when no rule applies,
        /// the parameter is sensitive, or the derived value would violate the
        /// manifest's own constraints (allowed values / pattern).
        /// </summary>
        public static Suggestion? For(TerraformTemplateParameter parameter, Context context)
        {
            if (parameter.Sensitive) return null;
            var rule = parameter.Suggest;
            if (rule is null && !NameRules.TryGetValue(parameter.Name, out rule)) return null;

            var suggestion = Apply(rule!, context);
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

        /// <summary>
        /// Environment tier derived from tokens in the environment name
        /// (e.g. "GMA QA 01" → qa, "LTRM PREPROD" → pp). Falls back to the
        /// environment's production flag when no token matches.
        /// </summary>
        public static string EnvironmentTier(Context context, out string reason)
        {
            foreach (Match token in Tokens.Matches(context.EnvironmentName.ToLowerInvariant()))
            {
                if (TierAliases.TryGetValue(token.Value, out var tier))
                {
                    reason = $"From \"{token.Value.ToUpperInvariant()}\" in environment name \"{context.EnvironmentName}\".";
                    return tier;
                }
            }
            reason = context.EnvironmentIsProd
                ? $"Environment \"{context.EnvironmentName}\" is flagged production."
                : $"Environment \"{context.EnvironmentName}\" is not flagged production.";
            return context.EnvironmentIsProd ? "pr" : "dv";
        }

        private static bool IsProduction(Context context)
        {
            var tier = EnvironmentTier(context, out _);
            return context.EnvironmentIsProd || tier == "pr" || tier == "dr";
        }

        private static Suggestion? Apply(string rule, Context context)
        {
            switch (rule)
            {
                case Hints.DorcEnvironment:
                {
                    var slug = NormaliseEnvironmentName(context.EnvironmentName);
                    return slug.Length == 0 ? null : new Suggestion(slug, $"DOrc environment \"{context.EnvironmentName}\".");
                }
                case Hints.EnvironmentTier:
                {
                    var tier = EnvironmentTier(context, out var reason);
                    return new Suggestion(tier, reason);
                }
                case Hints.IsProd:
                    return new Suggestion(IsProduction(context) ? "true" : "false",
                        $"Environment \"{context.EnvironmentName}\" {(IsProduction(context) ? "is" : "is not")} production.");
                case Hints.CloudByTier:
                    return IsProduction(context)
                        ? new Suggestion(ProdCloudName, "Production workloads use the prod Aiven cloud.")
                        : new Suggestion(NonProdCloudName, "Non-production workloads use the nprod Aiven cloud.");
                case Hints.TerminationProtection:
                {
                    var tier = EnvironmentTier(context, out _);
                    return tier == "dv"
                        ? new Suggestion("false", "Development tier resources are expected to be rebuilt freely.")
                        : new Suggestion("true", $"Protect {tier.ToUpperInvariant()} tier resources from accidental destroy.");
                }
                case Hints.BusinessVertical:
                    return string.IsNullOrEmpty(context.BusinessVertical)
                        ? new Suggestion(DefaultBusinessVertical, "No business vertical on the project; SEFE trading default. Set it in Project Ref Data.")
                        : new Suggestion(context.BusinessVertical!, "Business vertical configured on the project.");
                case Hints.ApplicationCode:
                    return string.IsNullOrEmpty(context.ApplicationCode)
                        ? null
                        : new Suggestion(context.ApplicationCode!, "Application code configured on the project.");
                case Hints.Scope:
                {
                    var tier = EnvironmentTier(context, out _);
                    return tier == "dv"
                        ? new Suggestion("tst", "Development tier topics default to the test scope.")
                        : new Suggestion("gbl", "Global scope unless the topic is local or user-specific.");
                }
                case Hints.Location:
                    return new Suggestion(DefaultLocation, "SEFE primary Azure region.");
                default:
                    return null;
            }
        }
    }
}

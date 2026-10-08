using System.Text.RegularExpressions;

namespace Dorc.TerraformRunner.State
{
    // Renders the provider configuration a catalog stock module cannot
    // declare for itself. The module contract (docs/Terraform/MODULE-CONTRACT.md)
    // forbids provider blocks inside modules - the consuming root supplies
    // them - but catalog deployments run the module AS the root, so DOrc is
    // the consumer and must render the configuration. azurerm is the only
    // provider needing this in v1: it refuses to plan without an explicit
    // `features {}` block. Authentication comes from per-environment
    // service-principal properties when configured (see
    // TerraformArmCredentials) and otherwise from the runner's environment
    // (ARM_* variables / managed identity), same as every other source type.
    public static class TerraformProviderRenderer
    {
        public const string ProviderFileName = "_dorc_providers.tf";

        // Well-known DOrc environment property naming the Azure subscription
        // catalog deployments for that environment target. Set it per
        // environment (dev envs -> DV subscription, QA/UAT -> NP, prod -> PR
        // per the SEFE subscription standard); when absent the runner
        // identity's default subscription applies.
        public const string SubscriptionIdPropertyName = "TerraformSubscriptionId";

        // Matches a `provider "azurerm" { ... }` block so its body can be
        // inspected. The module contract permits ALIASED provider blocks
        // (`provider "azurerm" { alias = "hub" ... }`) for cross-scope
        // resources; an aliased block is NOT a default provider
        // configuration, so its presence must not suppress rendering - only
        // a DEFAULT (alias-less) block does.
        private static readonly Regex AzureRmProviderBlockRegex = new(
            @"\bprovider\s*""azurerm""\s*\{",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ProviderAliasRegex = new(
            @"\balias\s*=",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AzureRmRequirementRegex = new(
            @"hashicorp/azurerm",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Writes `provider "azurerm" { features {} }` into the working
        // directory root when the root module requires the azurerm provider
        // (declares hashicorp/azurerm in required_providers) but configures
        // no provider block of its own. No-op otherwise, so a module that
        // legitimately ships a provider block, or one built on a different
        // provider, is left untouched. Only root-level *.tf files are
        // consulted - Terraform resolves provider configuration from the
        // root module only.
        //
        // subscriptionId, when supplied, pins the azurerm provider to that
        // subscription (`subscription_id = "..."`) so each DOrc environment
        // can target its own subscription. It must be a GUID: the value
        // comes from an environment property and is interpolated into a .tf
        // file, so anything non-GUID is rejected rather than written out.
        // The state backend is configured separately and is NOT affected.
        public static void WriteAzureRmIfRequired(string workingDirectory, string? subscriptionId = null)
        {
            if (string.IsNullOrEmpty(workingDirectory)) throw new ArgumentException("workingDirectory required", nameof(workingDirectory));
            if (workingDirectory.Contains(".."))
            {
                throw new ArgumentException("workingDirectory must not contain parent-directory segments", nameof(workingDirectory));
            }
            Guid? subscriptionGuid = null;
            if (!string.IsNullOrWhiteSpace(subscriptionId))
            {
                if (!Guid.TryParse(subscriptionId.Trim(), out var parsed))
                {
                    throw new ArgumentException(
                        $"'{SubscriptionIdPropertyName}' must be an Azure subscription GUID; got '{subscriptionId}'.",
                        nameof(subscriptionId));
                }
                subscriptionGuid = parsed;
            }
            if (!Directory.Exists(workingDirectory)) return;

            var requiresAzureRm = false;
            foreach (var content in Directory
                         .EnumerateFiles(workingDirectory, "*.tf", SearchOption.TopDirectoryOnly)
                         .Select(File.ReadAllText))
            {
                if (HasDefaultAzureRmProviderBlock(content))
                {
                    return; // module supplies its own default provider config
                }
                if (AzureRmRequirementRegex.IsMatch(content))
                {
                    requiresAzureRm = true;
                }
            }

            if (!requiresAzureRm) return;

            var path = Path.Join(workingDirectory, ProviderFileName);
            var subscriptionLine = subscriptionGuid is null
                ? string.Empty
                : $"  subscription_id = \"{subscriptionGuid.Value:D}\"\n";
            File.WriteAllText(
                path,
                "# Generated by DOrc - DO NOT EDIT.\n" +
                "# Catalog modules follow the module contract and declare no provider\n" +
                "# blocks; DOrc supplies the root-module provider configuration.\n" +
                "provider \"azurerm\" {\n" +
                subscriptionLine +
                "  features {}\n" +
                "}\n");
        }

        // True only when the content declares a DEFAULT azurerm provider
        // block - one with no `alias` argument in its body. An aliased-only
        // block leaves the default provider unconfigured, so it must not
        // suppress rendering. Uses a brace-balanced body scan (the same
        // approach as TerraformBackendValidator) rather than a flat regex so
        // an `alias` in a sibling block does not mask a defaultless one.
        private static bool HasDefaultAzureRmProviderBlock(string content)
        {
            foreach (var bodyStart in AzureRmProviderBlockRegex.Matches(content)
                         .Cast<Match>()
                         .Select(m => m.Index + m.Length))
            {
                var bodyEnd = FindMatchingClose(content, bodyStart);
                if (bodyEnd < 0) continue; // unbalanced; ignore this match
                var body = content.Substring(bodyStart, bodyEnd - bodyStart);
                if (!ProviderAliasRegex.IsMatch(body))
                {
                    return true; // a default (alias-less) provider block
                }
            }
            return false;
        }

        private static int FindMatchingClose(string content, int afterOpenBrace)
        {
            var depth = 1;
            for (var i = afterOpenBrace; i < content.Length; i++)
            {
                if (content[i] == '{') depth++;
                else if (content[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}

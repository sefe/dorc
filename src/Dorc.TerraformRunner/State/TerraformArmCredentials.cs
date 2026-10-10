using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.TerraformRunner.State
{
    // Per-environment Azure service-principal credentials for terraform
    // execution. Each DOrc environment (and therefore each subscription) can
    // own its own identity instead of sharing the runner host's ambient
    // credential: set the three well-known properties below on the
    // environment (TerraformClientSecret as a SECURE property) and the runner
    // injects them as ARM_* variables on the terraform process only - they
    // never touch the host's machine or service environment, and other
    // environments' deployments never see them. When the properties are
    // absent the runner falls back to the host's ambient identity (ARM_* in
    // the service environment, managed identity, or a cached `az login`),
    // preserving the previous behaviour for environments not yet migrated.
    public sealed class TerraformArmCredentials
    {
        // Well-known DOrc environment property names. ClientSecret MUST be
        // stored as a secure property so it is encrypted at rest and flagged
        // sensitive on the request; the runner additionally redacts its value
        // from terraform output regardless of the flag.
        public const string ClientIdPropertyName = "TerraformClientId";
        public const string ClientSecretPropertyName = "TerraformClientSecret";
        public const string TenantIdPropertyName = "TerraformTenantId";

        // Optional secure property holding an Aiven API token for modules
        // using the aiven/aiven provider. Injected as AIVEN_TOKEN on the
        // terraform process only; independent of the ARM_* credentials.
        public const string AivenApiTokenPropertyName = "TerraformAivenApiToken";

        public string ClientId { get; }
        public string ClientSecret { get; }
        public string TenantId { get; }

        private TerraformArmCredentials(string clientId, string clientSecret, string tenantId)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            TenantId = tenantId;
        }

        /// <summary>
        /// Resolves per-environment credentials from the deployment property
        /// set. Returns null when none of the credential properties are set
        /// (ambient host identity applies). Throws when the configuration is
        /// partial - a half-configured environment silently deploying as the
        /// shared host identity is exactly the surprise this feature removes.
        /// </summary>
        public static TerraformArmCredentials? Resolve(IDictionary<string, VariableValue>? properties)
        {
            var clientId = GetValue(properties, ClientIdPropertyName);
            var clientSecret = GetValue(properties, ClientSecretPropertyName);
            var tenantId = GetValue(properties, TenantIdPropertyName);

            if (clientId is null && clientSecret is null && tenantId is null)
            {
                return null;
            }

            var missing = new List<string>();
            if (clientId is null) missing.Add(ClientIdPropertyName);
            if (clientSecret is null) missing.Add(ClientSecretPropertyName);
            if (tenantId is null) missing.Add(TenantIdPropertyName);
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "Partial Terraform credential configuration: " +
                    $"missing environment propert{(missing.Count == 1 ? "y" : "ies")} {string.Join(", ", missing)}. " +
                    $"Set all of {ClientIdPropertyName}, {ClientSecretPropertyName} (secure) and {TenantIdPropertyName}, " +
                    "or none of them to use the runner host's ambient Azure identity.");
            }

            // ClientId and TenantId are GUIDs by definition. The secret is
            // free-form. Rejecting non-GUIDs here turns a typo into a clear
            // configuration error instead of an opaque Azure AD failure.
            if (!Guid.TryParse(clientId, out _))
            {
                throw new InvalidOperationException(
                    $"'{ClientIdPropertyName}' must be the service principal's application (client) ID GUID; got '{clientId}'.");
            }
            if (!Guid.TryParse(tenantId, out _))
            {
                throw new InvalidOperationException(
                    $"'{TenantIdPropertyName}' must be the Entra tenant ID GUID; got '{tenantId}'.");
            }

            return new TerraformArmCredentials(clientId!, clientSecret!, tenantId!);
        }

        /// <summary>
        /// The ARM_* variables terraform's azurerm provider (and backend)
        /// reads for service-principal authentication. Scoped to the
        /// terraform child process; never applied to the host environment.
        /// </summary>
        public IReadOnlyDictionary<string, string> ToArmEnvironment(string? subscriptionId = null)
        {
            var env = new Dictionary<string, string>
            {
                ["ARM_CLIENT_ID"] = ClientId,
                ["ARM_CLIENT_SECRET"] = ClientSecret,
                ["ARM_TENANT_ID"] = TenantId,
            };
            if (!string.IsNullOrWhiteSpace(subscriptionId))
            {
                env["ARM_SUBSCRIPTION_ID"] = subscriptionId.Trim();
            }
            return env;
        }

        /// <summary>
        /// Resolves the optional per-environment Aiven API token. Returns
        /// null when the property is not set.
        /// </summary>
        public static string? ResolveAivenApiToken(IDictionary<string, VariableValue>? properties)
            => GetValue(properties, AivenApiTokenPropertyName);

        private static string? GetValue(IDictionary<string, VariableValue>? properties, string name)
        {
            if (properties is null || !properties.TryGetValue(name, out var variable))
            {
                return null;
            }
            var value = variable?.Value?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}

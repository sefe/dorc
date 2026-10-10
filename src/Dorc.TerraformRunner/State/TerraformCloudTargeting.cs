using System.Text.Json;
using Dorc.ApiModel;
using Dorc.ApiModel.MonitorRunnerApi;

namespace Dorc.TerraformRunner.State
{
    // Resolves the Azure subscription a catalog deployment targets from the
    // environment's attached cloud resources (the Cloud tab on environment
    // details). A cloud resource with resource type "Subscription" names the
    // subscription the environment's terraform deployments run against, which
    // keeps targeting with the environment model itself instead of a
    // free-floating property. The TerraformSubscriptionId property remains as
    // a fallback for environments not yet carrying cloud resources.
    public static class TerraformCloudTargeting
    {
        // Well-known resource type marking the environment's target Azure
        // subscription. Matching is case-insensitive; the GUID is read from
        // ResourceIdentifier first, then from the Subscription field.
        public const string SubscriptionResourceType = "Subscription";

        public sealed record SubscriptionTarget(string SubscriptionId, string ResourceName);

        /// <summary>
        /// Resolves the target subscription from the EnvironmentCloudResources
        /// deployment property. Returns null when the environment has no cloud
        /// resources, none of them designates a subscription, or the
        /// designation is ambiguous (in which case <paramref name="warning"/>
        /// explains what was found so the operator can fix the environment).
        /// </summary>
        public static SubscriptionTarget? ResolveSubscription(
            IDictionary<string, VariableValue>? properties,
            out string? warning)
        {
            warning = null;

            var resources = ReadCloudResources(properties);
            if (resources.Length == 0) return null;

            var candidates = resources
                .Where(r => string.Equals(r.ResourceType?.Trim(), SubscriptionResourceType,
                    StringComparison.OrdinalIgnoreCase))
                .Select(r => (Resource: r, SubscriptionId: ExtractSubscriptionGuid(r)))
                .ToArray();

            if (candidates.Length == 0) return null;

            var invalid = candidates.Where(c => c.SubscriptionId is null).ToArray();
            if (invalid.Length > 0)
            {
                warning =
                    $"Cloud resource{(invalid.Length == 1 ? "" : "s")} " +
                    $"{string.Join(", ", invalid.Select(c => $"'{c.Resource.Name}'"))} of type " +
                    $"'{SubscriptionResourceType}' carr{(invalid.Length == 1 ? "ies" : "y")} no subscription GUID " +
                    "in the resource identifier or subscription field; " +
                    "set the subscription GUID on the environment's cloud resource.";
            }

            var valid = candidates
                .Where(c => c.SubscriptionId is not null)
                .ToArray();
            if (valid.Length == 0) return null;

            var distinct = valid
                .Select(c => c.SubscriptionId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinct.Length > 1)
            {
                warning =
                    "Ambiguous subscription targeting: the environment carries multiple " +
                    $"'{SubscriptionResourceType}' cloud resources naming different subscriptions " +
                    $"({string.Join(", ", valid.Select(c => $"'{c.Resource.Name}' -> {c.SubscriptionId}"))}). " +
                    "Keep a single subscription cloud resource per environment.";
                return null;
            }

            var chosen = valid[0];
            return new SubscriptionTarget(chosen.SubscriptionId!, chosen.Resource.Name ?? "");
        }

        // The pipe's VariableValueJsonConverter round-trips the typed array,
        // so in the runner Value is a VariableValueCloudResources[]. The JSON
        // fallbacks keep resolution working should the value ever arrive as a
        // raw JsonElement or JSON string instead.
        private static VariableValueCloudResources[] ReadCloudResources(
            IDictionary<string, VariableValue>? properties)
        {
            if (properties is null
                || !properties.TryGetValue(PropertyValueScopeOptionsFixed.EnvironmentCloudResources, out var variable)
                || variable?.Value is null)
            {
                return Array.Empty<VariableValueCloudResources>();
            }

            switch (variable.Value)
            {
                case VariableValueCloudResources[] typed:
                    return typed;
                case IEnumerable<VariableValueCloudResources> sequence:
                    return sequence.ToArray();
            }

            var json = variable.Value switch
            {
                JsonElement element => element.ValueKind == JsonValueKind.Array ? element.GetRawText() : null,
                string s => s,
                _ => null
            };
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<VariableValueCloudResources>();

            try
            {
                return JsonSerializer.Deserialize<VariableValueCloudResources[]>(
                           json,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? Array.Empty<VariableValueCloudResources>();
            }
            catch (JsonException)
            {
                return Array.Empty<VariableValueCloudResources>();
            }
        }

        private static string? ExtractSubscriptionGuid(VariableValueCloudResources resource)
        {
            if (Guid.TryParse(resource.ResourceIdentifier?.Trim(), out var fromIdentifier))
            {
                return fromIdentifier.ToString("D");
            }
            if (Guid.TryParse(resource.Subscription?.Trim(), out var fromSubscription))
            {
                return fromSubscription.ToString("D");
            }
            return null;
        }
    }
}

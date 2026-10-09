using System.Text.Json;
using Dorc.ApiModel;

namespace Dorc.TerraformRunner.State
{
    // Extracts the cloud resources a successful apply left in the terraform
    // state (`terraform show -json`) so the Monitor can register them against
    // the target environment's Cloud tab. Only managed resources count - data
    // sources reference existing infrastructure that the deployment neither
    // created nor owns.
    public static class TerraformAppliedResources
    {
        // ResourceIdentifier column width (CloudResourceEntityTypeConfiguration).
        private const int MaxResourceIdentifierLength = 500;

        /// <summary>
        /// Parses `terraform show -json` output into cloud resource models.
        /// Returns an empty list for unparseable or empty state rather than
        /// throwing: registration is a best-effort byproduct of the apply and
        /// must never fail a deployment that terraform itself completed.
        /// </summary>
        public static IReadOnlyList<CloudResourceApiModel> ParseShowJson(string? showJson)
        {
            if (string.IsNullOrWhiteSpace(showJson)) return Array.Empty<CloudResourceApiModel>();

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(showJson);
            }
            catch (JsonException)
            {
                return Array.Empty<CloudResourceApiModel>();
            }

            using (document)
            {
                if (!document.RootElement.TryGetProperty("values", out var values)
                    || values.ValueKind != JsonValueKind.Object
                    || !values.TryGetProperty("root_module", out var rootModule)
                    || rootModule.ValueKind != JsonValueKind.Object)
                {
                    return Array.Empty<CloudResourceApiModel>();
                }

                var resources = new List<CloudResourceApiModel>();
                CollectModule(rootModule, resources);

                // The same resource can appear once per module instance when
                // modules are reused; the identifier is the real identity.
                return resources
                    .GroupBy(r => r.ResourceIdentifier ?? r.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToArray();
            }
        }

        private static void CollectModule(JsonElement module, List<CloudResourceApiModel> resources)
        {
            if (module.TryGetProperty("resources", out var moduleResources)
                && moduleResources.ValueKind == JsonValueKind.Array)
            {
                foreach (var resource in moduleResources.EnumerateArray())
                {
                    var mapped = MapResource(resource);
                    if (mapped is not null) resources.Add(mapped);
                }
            }

            if (module.TryGetProperty("child_modules", out var children)
                && children.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in children.EnumerateArray())
                {
                    CollectModule(child, resources);
                }
            }
        }

        private static CloudResourceApiModel? MapResource(JsonElement resource)
        {
            if (GetString(resource, "mode") != "managed") return null;

            var type = GetString(resource, "type");
            if (string.IsNullOrWhiteSpace(type)) return null;

            string? cloudName = null;
            string? identifier = null;
            if (resource.TryGetProperty("values", out var values)
                && values.ValueKind == JsonValueKind.Object)
            {
                // Not every provider calls it "name": Aiven kafka topics use
                // topic_name, and some resources only carry a database_name.
                cloudName = GetString(values, "name")
                    ?? GetString(values, "topic_name")
                    ?? GetString(values, "database_name");
                identifier = GetString(values, "id");
            }

            // Prefer the last segment of the cloud-side id (the resource's
            // actual name for ARM paths and Aiven project/service/name ids)
            // over the config block label (usually a meaningless "this"/"main").
            if (string.IsNullOrWhiteSpace(cloudName))
            {
                cloudName = LastIdentifierSegment(identifier) ?? GetString(resource, "name");
            }
            if (string.IsNullOrWhiteSpace(cloudName) && string.IsNullOrWhiteSpace(identifier)) return null;

            if (identifier is { Length: > MaxResourceIdentifierLength })
            {
                identifier = identifier[..MaxResourceIdentifierLength];
            }

            return new CloudResourceApiModel
            {
                Name = string.IsNullOrWhiteSpace(cloudName) ? identifier! : cloudName!,
                Provider = MapProvider(GetString(resource, "provider_name"), type),
                ResourceType = type,
                ResourceIdentifier = identifier ?? string.Empty,
                Subscription = ExtractSubscription(identifier),
                Tags = string.Empty
            };
        }

        // provider_name is the full source address, e.g.
        // registry.terraform.io/hashicorp/azurerm. Collapse the well-known
        // providers to the names operators use on the Cloud tab.
        private static string MapProvider(string? providerName, string resourceType)
        {
            var name = providerName ?? string.Empty;
            if (name.Contains("azurerm", StringComparison.OrdinalIgnoreCase)
                || name.Contains("azuread", StringComparison.OrdinalIgnoreCase)
                || resourceType.StartsWith("azurerm_", StringComparison.OrdinalIgnoreCase)
                || resourceType.StartsWith("azuread_", StringComparison.OrdinalIgnoreCase))
            {
                return "Azure";
            }
            if (name.Contains("aiven", StringComparison.OrdinalIgnoreCase)
                || resourceType.StartsWith("aiven_", StringComparison.OrdinalIgnoreCase))
            {
                return "Aiven";
            }

            var lastSegment = name.Split('/').LastOrDefault();
            return string.IsNullOrWhiteSpace(lastSegment) ? "Terraform" : lastSegment!;
        }

        private static string? LastIdentifierSegment(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return null;
            var segment = identifier.TrimEnd('/').Split('/').LastOrDefault();
            return string.IsNullOrWhiteSpace(segment) ? null : segment;
        }

        private static string? ExtractSubscription(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return null;

            const string marker = "/subscriptions/";
            var index = identifier.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;

            var start = index + marker.Length;
            var end = identifier.IndexOf('/', start);
            var candidate = end < 0 ? identifier[start..] : identifier[start..end];
            return Guid.TryParse(candidate, out var guid) ? guid.ToString("D") : null;
        }

        private static string? GetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}

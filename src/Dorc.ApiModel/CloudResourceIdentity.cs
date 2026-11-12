using System;

namespace Dorc.ApiModel
{
    /// <summary>
    /// Terraform types qualify provider-local identifiers (for example an Aiven
    /// topic and database can share an id). Azure ARM paths already contain the
    /// resource kind, so their Terraform type is correctable metadata. Only ARM
    /// paths are case-insensitive; opaque provider identifiers retain their case.
    /// </summary>
    public static class CloudResourceIdentity
    {
        public static bool Matches(string provider, string resourceType, string identifier,
            string otherProvider, string otherResourceType, string otherIdentifier) =>
            string.Equals(provider, otherProvider, StringComparison.OrdinalIgnoreCase)
            && (IsAzureResourceManagerIdentifier(provider, identifier)
                || string.Equals(resourceType, otherResourceType, StringComparison.Ordinal))
            && string.Equals(identifier, otherIdentifier, IdentifierComparison(provider, identifier));

        public static string FingerprintInput(string provider, string resourceType, string identifier) =>
            System.Text.Json.JsonSerializer.Serialize(new[]
            {
                provider.ToUpperInvariant(),
                IsAzureResourceManagerIdentifier(provider, identifier) ? string.Empty : resourceType,
                IdentifierComparison(provider, identifier) == StringComparison.OrdinalIgnoreCase
                    ? identifier.ToUpperInvariant()
                    : identifier
            });

        public static bool IsAzureResourceManagerIdentifier(string provider, string identifier) =>
            string.Equals(provider, "Azure", StringComparison.OrdinalIgnoreCase)
            && (identifier.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase)
                || identifier.StartsWith("/providers/", StringComparison.OrdinalIgnoreCase));

        private static StringComparison IdentifierComparison(string provider, string identifier) =>
            IsAzureResourceManagerIdentifier(provider, identifier)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
    }
}

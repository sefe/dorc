using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Dorc.TerraformRunner.Logging
{
    public sealed class SensitivePropertyRedactor
    {
        public const string RedactedMarker = "[REDACTED]";

        private readonly SensitivePropertyRedactionOptions options;

        public SensitivePropertyRedactor(SensitivePropertyRedactionOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public IDictionary<string, string?> RedactProperties(IDictionary<string, string?> properties)
        {
            if (properties is null) throw new ArgumentNullException(nameof(properties));

            var result = new Dictionary<string, string?>(properties.Count, StringComparer.Ordinal);
            foreach (var kvp in properties)
            {
                result[kvp.Key] = IsSensitive(kvp.Key) ? RedactedMarker : kvp.Value;
            }
            return result;
        }

        public string RedactJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return json;
            }

            if (node is null) return json;
            RedactNode(node);
            return node.ToJsonString();
        }

        private void RedactNode(JsonNode node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(p => p.Key).ToList())
                    {
                        var child = obj[key];
                        if (child is JsonValue value && value.TryGetValue<string>(out _) && IsSensitive(key))
                        {
                            obj[key] = JsonValue.Create(RedactedMarker);
                        }
                        else if (child is not null)
                        {
                            RedactNode(child);
                        }
                    }
                    break;
                case JsonArray array:
                    foreach (var element in array)
                    {
                        if (element is not null) RedactNode(element);
                    }
                    break;
            }
        }

        private bool IsSensitive(string propertyName)
        {
            foreach (var pattern in options.Patterns)
            {
                if (pattern.IsMatch(propertyName)) return true;
            }
            return false;
        }
    }
}

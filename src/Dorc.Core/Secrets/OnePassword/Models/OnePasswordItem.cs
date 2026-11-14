using System.Text.Json.Serialization;

namespace Dorc.Core.Secrets.OnePassword
{
    public class OnePasswordItem
    {
        public const string MissingFieldSentinel = "Not Found";

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("fields")]
        public List<OnePasswordField> Fields { get; set; } = new();

        public string GetFieldValue(string fieldId)
        {
            if (string.IsNullOrWhiteSpace(fieldId))
            {
                return string.Empty;
            }

            var field = Fields.FirstOrDefault(f => string.Equals(f.Id, fieldId, StringComparison.OrdinalIgnoreCase))
                ?? Fields.FirstOrDefault(f => string.Equals(f.Label, fieldId, StringComparison.OrdinalIgnoreCase));

            if (field == null
                || string.IsNullOrWhiteSpace(field.Value)
                || string.Equals(field.Value.Trim(), MissingFieldSentinel, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return field.Value;
        }
    }
}

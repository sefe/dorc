using System.Text.Json.Serialization;

namespace Dorc.Core.Security.OnePassword
{
    public class OnePasswordItem
    {
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
            return Fields.FirstOrDefault(f => f.Id == fieldId)?.Value ?? "Not Found";
        }
    }
}

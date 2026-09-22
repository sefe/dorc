using System.Text.Json;

namespace Dorc.Api.Models
{
    /// <summary>
    /// ServiceNow Change Request response model.
    /// Uses sysparm_display_value=true so choice fields are human-readable display strings.
    /// </summary>
    public class SnChangeRequestResponse
    {
        public string sys_id { get; set; } = string.Empty;
        public string number { get; set; } = string.Empty;
        public string state { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string short_description { get; set; } = string.Empty;
        public string? start_date { get; set; }
        public string? end_date { get; set; }
        public string? approval { get; set; }
        public JsonElement? business_service { get; set; }

        /// <summary>
        /// Extracts the display value from a ServiceNow reference field,
        /// which may be a plain string or an object with "display_value"/"value" properties.
        /// </summary>
        public static string GetDisplayValue(JsonElement? element, string fallback = "N/A")
        {
            if (element == null) return fallback;
            var el = element.Value;

            if (el.ValueKind == JsonValueKind.String)
                return el.GetString() is { Length: > 0 } s ? s : fallback;

            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("display_value", out var dv) && dv.GetString() is { Length: > 0 } dvs)
                    return dvs;
                if (el.TryGetProperty("value", out var v) && v.GetString() is { Length: > 0 } vs)
                    return vs;
            }

            return fallback;
        }
    }

    /// <summary>ServiceNow table API array wrapper: { "result": [...] }</summary>
    public class SnResultArray<T>
    {
        public T[] result { get; set; } = Array.Empty<T>();
    }

    /// <summary>ServiceNow table API single-object wrapper: { "result": {...} }</summary>
    public class SnResultObject<T>
    {
        public T result { get; set; } = default!;
    }
}

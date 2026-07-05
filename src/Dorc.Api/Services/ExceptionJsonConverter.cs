using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dorc.Api.Services
{
    public class ExceptionJsonConverter : JsonConverter<Exception>
    {
        public override Exception Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type typeToConvert)
        {
            return typeof(Exception).IsAssignableFrom(typeToConvert);
        }

        public override void Write(Utf8JsonWriter writer, Exception value, JsonSerializerOptions options)
        {
            // Deliberately does NOT serialize the stack trace, the inner-exception
            // chain, or the fully-qualified type. Those disclose internal paths,
            // dependency internals, and potentially connection strings to any
            // caller that triggers a handled exception returned via
            // StatusCode(500, e). Emit only a short, safe shape (matching
            // DefaultExceptionHandler for unhandled exceptions).
            writer.WriteStartObject();
            writer.WriteString(nameof(Type), value.GetType().Name);
            writer.WriteString(nameof(Exception.Message), "An unexpected error occurred");
            writer.WriteString("ExceptionMessage", value.Message);
            writer.WriteEndObject();
        }
    }
}

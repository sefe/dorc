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
            // chain, the fully-qualified type, or the raw exception message. All of
            // those disclose internals to any caller that triggers a handled
            // exception returned via StatusCode(500, e) — the top-level message in
            // particular commonly carries sensitive detail (e.g. a SqlException
            // "Login failed for user 'X'... Cannot open database 'Y'"). The full
            // exception is logged server-side (DefaultExceptionHandler); the client
            // gets only a short, safe shape.
            writer.WriteStartObject();
            writer.WriteString(nameof(Type), value.GetType().Name);
            writer.WriteString(nameof(Exception.Message), "An unexpected error occurred");
            writer.WriteEndObject();
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dorc.Api.Windows.Infrastructure
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
            writer.WriteStartObject();
            writer.WriteString(nameof(Exception.Message), value.Message);

            if (value.InnerException != null)
            {
                writer.WriteStartObject(nameof(Exception.InnerException));
                Write(writer, value.InnerException, options);
                writer.WriteEndObject();
            }

            if (value.StackTrace != null)
            {
                writer.WriteString(nameof(Exception.StackTrace), value.StackTrace);
            }

            writer.WriteString(nameof(Type), value.GetType().ToString());
            writer.WriteEndObject();
        }
    }
}

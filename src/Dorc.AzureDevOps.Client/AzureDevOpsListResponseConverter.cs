using System.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dorc.AzureDevOps.Client
{
    /// <summary>
    /// Azure DevOps list endpoints return an envelope of the form
    /// <c>{"count": n, "value": [...]}</c>, while the vendored OpenAPI spec
    /// (build.json) declares the responses as bare arrays. This converter
    /// unwraps the envelope during deserialization so the generated client
    /// can remain pure generator output.
    /// </summary>
    public class AzureDevOpsListResponseConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(List<>);
        }

        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var token = JToken.Load(reader);
            if (token.Type == JTokenType.Object)
            {
                token = token["value"]
                    ?? throw new JsonSerializationException(
                        $"Expected an array or a count/value envelope for {objectType}, " +
                        "but the object has no 'value' property.");
            }

            var elementType = objectType.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(objectType)!;
            foreach (var item in token)
            {
                list.Add(item.ToObject(elementType, serializer));
            }
            return list;
        }
    }
}

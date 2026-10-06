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

            // Load the tree with date recognition switched off. The reader
            // defaults to DateParseHandling.DateTime, which turns any string
            // that merely looks like a timestamp into a DateTime token; the
            // ToObject below would then render it back to a string in the
            // current culture's format rather than the text the server sent,
            // silently rewriting values such as Build.SourceVersion and any
            // string in Tags or TriggerInfo. Newtonsoft's normal path reads
            // string-typed members as raw text, and so must this one.
            var originalDateParseHandling = reader.DateParseHandling;
            JToken token;
            try
            {
                reader.DateParseHandling = DateParseHandling.None;
                token = JToken.Load(reader);
            }
            finally
            {
                reader.DateParseHandling = originalDateParseHandling;
            }

            if (token.Type == JTokenType.Object)
            {
                token = token["value"]
                    ?? throw new JsonSerializationException(
                        $"Expected an array or a count/value envelope for {objectType}, " +
                        "but the object has no 'value' property.");
            }

            // Anything that is not an array by this point must fail loudly.
            // Enumerating a JValue yields nothing, so without this a payload of
            // {"count":1,"value":null} — or a bare scalar, or an error body —
            // would deserialize to an empty list and read downstream as "no
            // builds" or "no artifacts" rather than as a failed call. Note
            // that `token["value"]` returns a JValue of type Null for an
            // explicit null, never a C# null, so the ?? above cannot catch it.
            if (token.Type != JTokenType.Array)
            {
                throw new JsonSerializationException(
                    $"Expected an array or a count/value envelope for {objectType}, " +
                    $"but the payload was {token.Type}.");
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

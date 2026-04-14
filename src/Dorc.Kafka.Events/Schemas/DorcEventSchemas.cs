using System.Text.Json.Nodes;
using Chr.Avro.Abstract;
using Chr.Avro.Representation;
using Dorc.Core.Events;

namespace Dorc.Kafka.Events.Schemas;

/// <summary>
/// Canonical Chr.Avro-generated schemas for the S-003 in-scope event
/// contracts. Output is JSON-key-sorted (alphabetical) so the emitted form
/// is stable across runs and diffable against the registry-acknowledged
/// snapshots under docs/kafka-migration/schemas/latest/. Deterministic:
/// two calls for the same CLR type produce byte-identical Avro JSON.
/// </summary>
public static class DorcEventSchemas
{
    public static string GenerateJsonFor<T>()
    {
        var schemaBuilder = new SchemaBuilder();
        var jsonWriter = new JsonSchemaWriter();
        var schema = schemaBuilder.BuildSchema<T>();
        var natural = jsonWriter.Write(schema);
        return Canonicalise(JsonNode.Parse(natural))!.ToJsonString();
    }

    public static string GenerateRequestEventSchema()
        => GenerateJsonFor<DeploymentRequestEventData>();

    public static string GenerateResultEventSchema()
        => GenerateJsonFor<DeploymentResultEventData>();

    private static JsonNode? Canonicalise(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var ordered = new JsonObject();
                foreach (var kv in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                    ordered[kv.Key] = Canonicalise(kv.Value?.DeepClone());
                return ordered;
            case JsonArray arr:
                var newArr = new JsonArray();
                foreach (var item in arr)
                    newArr.Add(Canonicalise(item?.DeepClone()));
                return newArr;
            default:
                return node;
        }
    }
}

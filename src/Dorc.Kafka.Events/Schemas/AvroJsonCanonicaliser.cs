using System.Text.Json.Nodes;

namespace Dorc.Kafka.Events.Schemas;

/// <summary>
/// Canonicalises Avro schema JSON into a stable, diffable rendering: object
/// keys are recursively sorted with ordinal comparison; array element order
/// (semantically significant in Avro — record field order, union branch
/// order) is preserved. Shared by <see cref="DorcEventSchemas"/>, the PR
/// schema gate, and the generate-schemas / snapshot-schemas tools so every
/// schema-comparison surface agrees on ONE canonical form and byte-equality
/// checks between them can never drift.
/// </summary>
public static class AvroJsonCanonicaliser
{
    public static JsonNode? Canonicalise(JsonNode? node)
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

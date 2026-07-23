using System.Text.Json.Nodes;
using Chr.Avro.Abstract;
using Chr.Avro.Representation;
using Dorc.Core.Events;
using Dorc.Kafka.ErrorLog;

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
        return AvroJsonCanonicaliser.Canonicalise(JsonNode.Parse(natural))!.ToJsonString();
    }

    public static string GenerateRequestEventSchema()
        => GenerateJsonFor<DeploymentRequestEventData>();

    public static string GenerateResultEventSchema()
        => GenerateJsonFor<DeploymentResultEventData>();

    public static string GenerateErrorEnvelopeSchema()
        => GenerateJsonFor<KafkaErrorEnvelope>();
}

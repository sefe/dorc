using Chr.Avro.Abstract;
using Chr.Avro.Representation;
using Dorc.Core.Events;

namespace Dorc.Kafka.Events.Schemas;

/// <summary>
/// Canonical Chr.Avro-generated schemas for the S-003 in-scope event
/// contracts. <see cref="GenerateJson{T}"/> is deterministic: two calls
/// against the same CLR type produce byte-identical Avro JSON.
/// </summary>
public static class DorcEventSchemas
{
    public static string GenerateJsonFor<T>()
    {
        var schemaBuilder = new SchemaBuilder();
        var jsonWriter = new JsonSchemaWriter();
        var schema = schemaBuilder.BuildSchema<T>();
        return jsonWriter.Write(schema);
    }

    public static string GenerateRequestEventSchema()
        => GenerateJsonFor<DeploymentRequestEventData>();

    public static string GenerateResultEventSchema()
        => GenerateJsonFor<DeploymentResultEventData>();
}

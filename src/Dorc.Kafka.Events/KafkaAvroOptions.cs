namespace Dorc.Kafka.Events;

public sealed class KafkaAvroOptions
{
    public const string SectionName = "Kafka:Avro";

    /// <summary>
    /// Optional overrides mapping .NET type FQN to subject name. The factory
    /// seeds its in-scope type set with the three S-003 contracts by default;
    /// this dictionary is carried forward for future subject-name
    /// customisation without a code edit.
    /// </summary>
    public Dictionary<string, string> SubjectOverrides { get; set; } = new();

    /// <summary>
    /// When <c>true</c>, the producer-side schema-registry serializer auto-
    /// registers an evolved schema on first publish. Default <c>false</c>:
    /// schemas must be pre-registered at PR-time via the AvroSchemaGate, so
    /// any runtime registration would bypass the gate's evolution-safety
    /// check. Operators can flip this to <c>true</c> in dev/compose stacks
    /// where schemas are registered lazily.
    /// </summary>
    public bool AllowAutomaticSchemaRegistration { get; set; } = false;
}

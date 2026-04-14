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
}

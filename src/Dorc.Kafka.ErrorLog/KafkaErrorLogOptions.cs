namespace Dorc.Kafka.ErrorLog;

public sealed class KafkaErrorLogOptions
{
    public const string SectionName = "Kafka:ErrorLog";

    public int RetentionDays { get; set; } = 30;

    public int MaxPayloadBytes { get; set; } = 65_536;

    public int PurgeBatchSize { get; set; } = 5_000;

    public int QueryMaxRowsCap { get; set; } = 10_000;
}

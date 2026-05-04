namespace Dorc.Kafka.Client.Configuration;

public sealed class KafkaClientOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = string.Empty;

    public KafkaAuthMode AuthMode { get; set; } = KafkaAuthMode.Plaintext;

    public KafkaSaslOptions Sasl { get; set; } = new();

    public string? SslCaLocation { get; set; }

    public KafkaSchemaRegistryOptions SchemaRegistry { get; set; } = new();

    public string? ConsumerGroupId { get; set; }

    public KafkaAutoOffsetReset AutoOffsetReset { get; set; } = KafkaAutoOffsetReset.Earliest;

    public bool EnableAutoCommit { get; set; } = false;

    public int SessionTimeoutMs { get; set; } = 30_000;

    public int HeartbeatIntervalMs { get; set; } = 10_000;

    public int MaxPollIntervalMs { get; set; } = 300_000;

    /// <summary>
    /// How often librdkafka emits a statistics JSON blob (consumed by
    /// <see cref="Observability.IKafkaConsumerMetrics"/> for lag and state
    /// gauges). 0 disables the callback. Default 30 seconds — frequent
    /// enough for human monitoring without flooding the metrics path.
    /// </summary>
    public int StatisticsIntervalMs { get; set; } = 30_000;
}

public sealed class KafkaSaslOptions
{
    public string Mechanism { get; set; } = "SCRAM-SHA-256";

    public string? Username { get; set; }

    public string? Password { get; set; }
}

public sealed class KafkaSchemaRegistryOptions
{
    public string? Url { get; set; }

    public string? BasicAuthUsername { get; set; }

    public string? BasicAuthPassword { get; set; }
}

public enum KafkaAutoOffsetReset
{
    Earliest,
    Latest,
    Error
}

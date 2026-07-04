using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Provisioning;
using Dorc.Kafka.Events.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Startup hook that provisions the / Kafka topics on an
/// idempotent best-effort basis. Owns the provisioning entry-point
/// because  lands first;  inherits it via the same hook.
///
/// :
/// - Topic creation with 12 partitions, RF from KafkaSubstrateOptions,
/// min.insync.replicas = 2 (or 1 for RF=1 dev).
/// - Existing topic with same partition count -> Information log, no-op.
/// - Existing topic with different partition count -> Warning log, no
/// throw (per-RequestId ordering is preserved for a fixed count).
/// - RF-rejection on single-broker dev -> Warning, no throw (producer
/// will fail-loud on first publish if topic genuinely missing).
///
/// The error-triage/verification policy itself lives in the shared
/// <see cref="IdempotentTopicProvisioner"/> core (also used by the DLQ
/// provisioner); this class owns only the topic set and their specs.
/// </summary>
public sealed class KafkaResultsStatusTopicProvisioner : IHostedService
{
    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly KafkaSubstrateOptions _substrateOptions;
    private readonly KafkaTopicsOptions _topics;
    private readonly ILogger<KafkaResultsStatusTopicProvisioner> _logger;

    public KafkaResultsStatusTopicProvisioner(
        IKafkaConnectionProvider connectionProvider,
        IOptions<KafkaSubstrateOptions> substrateOptions,
        IOptions<KafkaTopicsOptions> topics,
        ILogger<KafkaResultsStatusTopicProvisioner> logger)
    {
        _connectionProvider = connectionProvider;
        _substrateOptions = substrateOptions.Value;
        _topics = topics.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Admin config comes from the same connection provider the
        // producers/consumers use, so SASL + bootstrap are single-sourced.
        using var admin = new AdminClientBuilder(_connectionProvider.GetAdminConfig()).Build();
        await ProvisionAsync(admin, cancellationToken);
    }

    /// <summary>
    /// Internal seam so the error-policy paths (topic-exists verification,
    /// ACL denial, broker-unreachable) are unit-testable with a scripted
    /// admin client — the same pattern as the lock topic provisioner.
    /// </summary>
    internal async Task ProvisionAsync(IAdminClient admin, CancellationToken cancellationToken)
    {
        // Validator (KafkaTopicsOptionsValidator) guarantees non-empty values
        // post-startup, so iteration here may treat each property as non-null
        // without runtime defensive checks.
        var specs = new[]
        {
            _topics.ResultsStatus,
            // inherits — provisioned here so its consumer/producer can assume presence.
            _topics.RequestsNew,
            _topics.RequestsStatus
        }.Select(BuildTopicSpecification).ToArray();

        // One batched create for all three topics; the shared core triages
        // the broker's per-topic results and never lets a failure escape.
        await IdempotentTopicProvisioner.ProvisionAsync(admin, specs, "Kafka", _logger, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Requests-topic retention: 7 days. The requests consumers use
    /// per-replica consumer groups with <c>AutoOffsetReset.Earliest</c>, so a
    /// replica whose group is new (first deploy, or replaced replica id)
    /// replays the entire retained topic. With the broker-default retention
    /// (often weeks, or unbounded) that replay volume grows without limit;
    /// 7 days bounds it while leaving an ample operational replay window.
    /// </summary>
    internal const long RequestsTopicRetentionMs = 604_800_000L; // 7 days

    /// <summary>
    /// Shapes the topic-creation spec. <c>internal</c> so tests can pin the
    /// per-topic config (retention override on the two requests topics only;
    /// results.status keeps the broker default because its events are
    /// real-time signals consumed with <c>AutoOffsetReset.Latest</c>).
    /// </summary>
    internal TopicSpecification BuildTopicSpecification(string topic)
    {
        var rf = _substrateOptions.ResultsStatusReplicationFactor;
        var minIsr = rf >= 3 ? 2 : 1;
        var configs = new Dictionary<string, string>
        {
            ["min.insync.replicas"] = minIsr.ToString()
        };
        if (topic == _topics.RequestsNew || topic == _topics.RequestsStatus)
        {
            configs["retention.ms"] = RequestsTopicRetentionMs.ToString();
        }
        return new TopicSpecification
        {
            Name = topic,
            NumPartitions = 12,
            ReplicationFactor = rf,
            Configs = configs
        };
    }
}

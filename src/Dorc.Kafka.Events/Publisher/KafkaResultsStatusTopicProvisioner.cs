using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Events.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Startup hook that provisions the S-007/S-006 Kafka topics on an
/// idempotent best-effort basis. Owns the provisioning entry-point
/// because S-007 lands first; S-006 inherits it via the same hook.
///
/// Per SPEC-S-007 R-4:
///  - Topic creation with 12 partitions, RF from KafkaSubstrateOptions,
///    min.insync.replicas = 2 (or 1 for RF=1 dev).
///  - Existing topic with same partition count -> Information log, no-op.
///  - Existing topic with different partition count -> Warning log, no
///    throw (per-RequestId ordering is preserved for a fixed count).
///  - RF-rejection on single-broker dev -> Warning, no throw (producer
///    will fail-loud on first publish if topic genuinely missing).
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
        // Validator (KafkaTopicsOptionsValidator) guarantees non-empty values
        // post-startup, so iteration here may treat each property as non-null
        // without runtime defensive checks (SPEC-S-017 §4 R1).
        var topics = new[]
        {
            _topics.ResultsStatus,
            // S-006 inherits — provisioned here so its consumer/producer can assume presence.
            _topics.RequestsNew,
            _topics.RequestsStatus
        };

        // Derive admin-client config from the same connection provider the
        // producers/consumers use, so SASL + bootstrap are single-sourced.
        var producerConfig = _connectionProvider.GetProducerConfig();
        var adminConfig = new AdminClientConfig
        {
            BootstrapServers = producerConfig.BootstrapServers,
            SecurityProtocol = producerConfig.SecurityProtocol,
            SaslMechanism = producerConfig.SaslMechanism,
            SaslUsername = producerConfig.SaslUsername,
            SaslPassword = producerConfig.SaslPassword,
            SslCaLocation = producerConfig.SslCaLocation
        };

        using var admin = new AdminClientBuilder(adminConfig).Build();

        foreach (var topic in topics)
            await ProvisionOneAsync(admin, topic, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ProvisionOneAsync(IAdminClient admin, string topic, CancellationToken cancellationToken)
    {
        var rf = _substrateOptions.ResultsStatusReplicationFactor;
        var minIsr = rf >= 3 ? 2 : 1;
        var spec = new TopicSpecification
        {
            Name = topic,
            NumPartitions = 12,
            ReplicationFactor = rf,
            Configs = new Dictionary<string, string>
            {
                ["min.insync.replicas"] = minIsr.ToString()
            }
        };

        try
        {
            await admin.CreateTopicsAsync(new[] { spec });
            _logger.LogInformation(
                "Kafka topic created: topic={Topic} partitions={Partitions} rf={Rf} minIsr={MinIsr}",
                topic, spec.NumPartitions, spec.ReplicationFactor, minIsr);
        }
        catch (CreateTopicsException ex) when (ex.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
        {
            await VerifyPartitionCountAsync(admin, topic, spec.NumPartitions, cancellationToken);
        }
        catch (CreateTopicsException ex)
        {
            var code = ex.Results[0].Error.Code;
            var reason = ex.Results[0].Error.Reason;
            // Distinguish broker-side rejection classes so an ACL
            // misconfiguration is loud (LogError), while RF-rejection on
            // single-broker dev is expected noise (LogWarning). Either way
            // the provisioner does NOT throw — startup continues and the
            // producer fail-loud on first publish is the backstop.
            if (code is ErrorCode.TopicAuthorizationFailed
                     or ErrorCode.ClusterAuthorizationFailed)
            {
                _logger.LogError(
                    "Kafka topic create denied by broker ACL: topic={Topic} code={Code} reason={Reason}. Check service-account permissions.",
                    topic, code, reason);
            }
            else if (code is ErrorCode.InvalidReplicationFactor
                          or ErrorCode.PolicyViolation)
            {
                _logger.LogWarning(
                    "Kafka topic create rejected (dev-style config issue): topic={Topic} code={Code} reason={Reason}.",
                    topic, code, reason);
            }
            else
            {
                _logger.LogError(
                    "Kafka topic create failed: topic={Topic} code={Code} reason={Reason}.",
                    topic, code, reason);
            }
        }
    }

    private async Task VerifyPartitionCountAsync(IAdminClient admin, string topic, int expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(3));
        var meta = metadata.Topics.FirstOrDefault(t => t.Topic == topic);
        if (meta is null)
        {
            _logger.LogWarning("Kafka topic existence ack conflict: topic={Topic} not present in metadata fetch.", topic);
            return;
        }

        var actual = meta.Partitions.Count;
        if (actual == expected)
        {
            _logger.LogInformation(
                "Kafka topic already present with expected partition count: topic={Topic} partitions={Partitions}",
                topic, actual);
        }
        else
        {
            _logger.LogWarning(
                "Kafka topic present with DIFFERENT partition count: topic={Topic} expected={Expected} actual={Actual}. Per-key ordering still holds for a fixed count, but this is topology drift the operator should reconcile.",
                topic, expected, actual);
        }

        await Task.CompletedTask;
    }
}

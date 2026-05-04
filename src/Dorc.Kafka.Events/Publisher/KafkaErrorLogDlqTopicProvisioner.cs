using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.Events.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Startup hook that provisions the per-source DLQ topics (post-K-2) on an
/// idempotent best-effort basis. Mirrors the posture of
/// <see cref="KafkaResultsStatusTopicProvisioner"/>:
///  - Topic creation with <see cref="KafkaErrorLogOptions.PartitionCount"/>
///    partitions, <see cref="KafkaErrorLogOptions.ReplicationFactor"/> RF,
///    <c>retention.ms</c> from <see cref="KafkaErrorLogOptions.RetentionMs"/>,
///    and <c>min.insync.replicas</c> = 2 (or 1 for RF=1 dev).
///  - Existing topic with same partition count -> Information log, no-op.
///  - Existing topic with different partition count -> Warning, no throw.
///  - Broker rejection (RF / ACL) -> log + continue; producer fails-loud on
///    first publish if the topic genuinely doesn't exist.
/// </summary>
public sealed class KafkaErrorLogDlqTopicProvisioner : IHostedService
{
    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly KafkaErrorLogOptions _errorLogOptions;
    private readonly KafkaTopicsOptions _topics;
    private readonly ILogger<KafkaErrorLogDlqTopicProvisioner> _logger;

    public KafkaErrorLogDlqTopicProvisioner(
        IKafkaConnectionProvider connectionProvider,
        IOptions<KafkaErrorLogOptions> errorLogOptions,
        IOptions<KafkaTopicsOptions> topics,
        ILogger<KafkaErrorLogDlqTopicProvisioner> logger)
    {
        _connectionProvider = connectionProvider;
        _errorLogOptions = errorLogOptions.Value;
        _topics = topics.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var dlqTopics = new[] { _topics.RequestsNewDlq };

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
        foreach (var topic in dlqTopics)
            await ProvisionOneAsync(admin, topic, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ProvisionOneAsync(IAdminClient admin, string topic, CancellationToken cancellationToken)
    {
        var rf = _errorLogOptions.ReplicationFactor;
        var minIsr = rf >= 3 ? 2 : 1;
        var spec = new TopicSpecification
        {
            Name = topic,
            NumPartitions = _errorLogOptions.PartitionCount,
            ReplicationFactor = rf,
            Configs = new Dictionary<string, string>
            {
                ["min.insync.replicas"] = minIsr.ToString(),
                ["retention.ms"] = _errorLogOptions.RetentionMs.ToString()
            }
        };

        try
        {
            await admin.CreateTopicsAsync(new[] { spec });
            _logger.LogInformation(
                "DLQ topic created: topic={Topic} partitions={Partitions} rf={Rf} minIsr={MinIsr} retentionMs={RetentionMs}",
                topic, spec.NumPartitions, spec.ReplicationFactor, minIsr, _errorLogOptions.RetentionMs);
        }
        catch (CreateTopicsException ex) when (ex.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
        {
            await VerifyPartitionCountAsync(admin, topic, spec.NumPartitions, cancellationToken);
        }
        catch (CreateTopicsException ex)
        {
            var code = ex.Results[0].Error.Code;
            var reason = ex.Results[0].Error.Reason;
            if (code is ErrorCode.TopicAuthorizationFailed or ErrorCode.ClusterAuthorizationFailed)
            {
                _logger.LogError(
                    "DLQ topic create denied by broker ACL: topic={Topic} code={Code} reason={Reason}. Check service-account permissions.",
                    topic, code, reason);
            }
            else if (code is ErrorCode.InvalidReplicationFactor or ErrorCode.PolicyViolation)
            {
                _logger.LogWarning(
                    "DLQ topic create rejected (dev-style config issue): topic={Topic} code={Code} reason={Reason}.",
                    topic, code, reason);
            }
            else
            {
                _logger.LogError(
                    "DLQ topic create failed: topic={Topic} code={Code} reason={Reason}.",
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
            _logger.LogWarning("DLQ topic existence ack conflict: topic={Topic} not present in metadata fetch.", topic);
            return;
        }

        var actual = meta.Partitions.Count;
        if (actual == expected)
        {
            _logger.LogInformation(
                "DLQ topic already present with expected partition count: topic={Topic} partitions={Partitions}",
                topic, actual);
        }
        else
        {
            _logger.LogWarning(
                "DLQ topic present with DIFFERENT partition count: topic={Topic} expected={Expected} actual={Actual}.",
                topic, expected, actual);
        }

        await Task.CompletedTask;
    }
}

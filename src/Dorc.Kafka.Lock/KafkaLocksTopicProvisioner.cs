using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock;

/// <summary>
/// Provisioning hook for the dorc.locks topic (SPEC-S-005b R-5). Partition
/// count is immutable post-cutover (ADR-S-005 §4 #2); the S-010 runbook
/// enforces that operationally. Mirrors the S-007 results-status provisioner
/// in error classification (ACL vs RF rejection vs other).
/// </summary>
public sealed class KafkaLocksTopicProvisioner : IHostedService
{
    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly KafkaLocksOptions _options;
    private readonly ILogger<KafkaLocksTopicProvisioner> _logger;

    public KafkaLocksTopicProvisioner(
        IKafkaConnectionProvider connectionProvider,
        IOptions<KafkaLocksOptions> options,
        ILogger<KafkaLocksTopicProvisioner> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
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

        var minIsr = _options.ReplicationFactor >= 3 ? 2 : 1;
        var spec = new TopicSpecification
        {
            Name = _options.Topic,
            NumPartitions = _options.PartitionCount,
            ReplicationFactor = _options.ReplicationFactor,
            Configs = new Dictionary<string, string>
            {
                ["min.insync.replicas"] = minIsr.ToString()
            }
        };

        try
        {
            await admin.CreateTopicsAsync(new[] { spec });
            _logger.LogInformation(
                "Kafka lock topic created: topic={Topic} partitions={Partitions} rf={Rf} minIsr={MinIsr}",
                _options.Topic, spec.NumPartitions, spec.ReplicationFactor, minIsr);
        }
        catch (CreateTopicsException ex) when (ex.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
        {
            VerifyPartitionCount(admin, _options.Topic, _options.PartitionCount);
        }
        catch (CreateTopicsException ex)
        {
            var code = ex.Results[0].Error.Code;
            var reason = ex.Results[0].Error.Reason;
            if (code is ErrorCode.TopicAuthorizationFailed or ErrorCode.ClusterAuthorizationFailed)
                _logger.LogError("Kafka lock topic create denied by ACL: code={Code} reason={Reason}", code, reason);
            else if (code is ErrorCode.InvalidReplicationFactor or ErrorCode.PolicyViolation)
                _logger.LogWarning("Kafka lock topic create rejected (dev config): code={Code} reason={Reason}", code, reason);
            else
                _logger.LogError("Kafka lock topic create failed: code={Code} reason={Reason}", code, reason);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void VerifyPartitionCount(IAdminClient admin, string topic, int expected)
    {
        var metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(3));
        var meta = metadata.Topics.FirstOrDefault(t => t.Topic == topic);
        if (meta is null)
        {
            _logger.LogWarning("Kafka lock topic existence ack conflict: topic={Topic} not in metadata", topic);
            return;
        }

        var actual = meta.Partitions.Count;
        if (actual == expected)
        {
            _logger.LogInformation(
                "Kafka lock topic present with expected partitions: topic={Topic} partitions={Partitions}",
                topic, actual);
        }
        else
        {
            _logger.LogWarning(
                "Kafka lock topic has DIFFERENT partition count: topic={Topic} expected={Expected} actual={Actual}. " +
                "Partition-count is immutable post-cutover (ADR-S-005 §4 #2); reconcile via S-010 runbook.",
                topic, expected, actual);
        }
    }
}

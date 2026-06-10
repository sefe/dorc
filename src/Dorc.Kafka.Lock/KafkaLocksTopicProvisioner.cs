using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock;

/// <summary>
/// Provisioning hook for the dorc.locks topic. Partition
/// count is immutable post-cutover; the  runbook
/// enforces that operationally.
///
/// Error policy:
/// <list type="bullet">
/// <item>Partition-count mismatch → fail fast (<see cref="InvalidOperationException"/>
/// stops the host): the configured count drives lock-key routing, so a
/// mismatch silently mis-routes locks → split-brain.</item>
/// <item>Broker unreachable at boot (<see cref="KafkaException"/>) → log error
/// and continue startup; the coordinator's consume loop retries connectivity.</item>
/// <item>Topic creation rejected (ACL, policy, anything other than
/// topic-already-exists) → log error including a clear "distributed locking
/// may be unavailable" warning, and continue.</item>
/// </list>
/// </summary>
public sealed class KafkaLocksTopicProvisioner : IHostedService
{
    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly KafkaLocksOptions _options;
    private readonly KafkaTopicsOptions _topics;
    private readonly ILogger<KafkaLocksTopicProvisioner> _logger;

    public KafkaLocksTopicProvisioner(
        IKafkaConnectionProvider connectionProvider,
        IOptions<KafkaLocksOptions> options,
        IOptions<KafkaTopicsOptions> topics,
        ILogger<KafkaLocksTopicProvisioner> logger)
    {
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _topics = topics.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        await ProvisionAsync(admin, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Provisioning core, separated from admin-client construction so the
    /// error policy is unit-testable against a scripted <see cref="IAdminClient"/>.
    /// </summary>
    internal async Task ProvisionAsync(IAdminClient admin, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var minIsr = _options.ReplicationFactor >= 3 ? 2 : 1;
        var spec = new TopicSpecification
        {
            Name = _topics.Locks,
            NumPartitions = _options.PartitionCount,
            ReplicationFactor = _options.ReplicationFactor,
            Configs = new Dictionary<string, string>
            {
                ["min.insync.replicas"] = minIsr.ToString()
            }
        };

        try
        {
            // The admin API takes no CancellationToken; honor StartAsync's token
            // via WaitAsync so a cancelled host start doesn't hang on a dead broker.
            await admin.CreateTopicsAsync(new[] { spec }).WaitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Kafka lock topic created: topic={Topic} partitions={Partitions} rf={Rf} minIsr={MinIsr}",
                _topics.Locks, spec.NumPartitions, spec.ReplicationFactor, minIsr);
        }
        catch (CreateTopicsException ex) when (ex.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
        {
            try
            {
                // Fail fast on mismatch: VerifyPartitionCount throws
                // InvalidOperationException, which deliberately escapes StartAsync
                // and stops the host (wrong routing → split-brain).
                VerifyPartitionCount(admin, _topics.Locks, _options.PartitionCount);
            }
            catch (KafkaException kex)
            {
                _logger.LogError(kex,
                    "Kafka lock topic partition-count verification failed (broker unreachable?): topic={Topic}. " +
                    "Continuing startup; distributed locking may be unavailable until connectivity is restored.",
                    _topics.Locks);
            }
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

            _logger.LogError(
                "Kafka lock topic could not be provisioned: topic={Topic}. Distributed locking may be UNAVAILABLE " +
                "until the topic exists — concurrent Monitor instances will not be mutually excluded.",
                _topics.Locks);
        }
        catch (KafkaException ex)
        {
            // Broker unreachable / timed out at boot. Crash-looping the host here
            // gains nothing — the coordinator's consume loop retries connectivity
            // and the next boot re-attempts provisioning.
            _logger.LogError(ex,
                "Kafka unreachable while provisioning lock topic: topic={Topic}. Continuing startup; the lock " +
                "consumer loop will retry. Distributed locking may be UNAVAILABLE until the topic exists.",
                _topics.Locks);
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
            _logger.LogCritical(
                "Kafka lock topic partition-count mismatch: topic={Topic} expected={Expected} actual={Actual}. " +
                "Lock hashing uses the configured count (KafkaLockCoordinator.cs), so a mismatch would make some " +
                "locks unacquirable. Partition-count is immutable post-cutover; " +
                "reconcile via the cutover runbook before restarting.",
                topic, expected, actual);
            throw new InvalidOperationException(
                $"Kafka lock topic '{topic}' has {actual} partitions but KafkaLocksOptions.PartitionCount is {expected}. " +
                "Refusing to start: reconcile via the cutover runbook.");
        }
    }
}

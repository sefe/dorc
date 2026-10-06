using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Provisioning;
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
///
/// The error-triage/verification policy is the shared
/// <see cref="IdempotentTopicProvisioner"/> core; this class owns only the
/// DLQ topic spec.
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
        using var admin = new AdminClientBuilder(_connectionProvider.GetAdminConfig()).Build();
        await ProvisionAsync(admin, cancellationToken);
    }

    /// <summary>
    /// Internal seam so the error-policy paths are unit-testable with a
    /// scripted admin client — same pattern as the other provisioners.
    /// </summary>
    internal async Task ProvisionAsync(IAdminClient admin, CancellationToken cancellationToken)
    {
        var rf = _errorLogOptions.ReplicationFactor;
        var minIsr = rf >= 3 ? 2 : 1;
        var specs = new[] { _topics.RequestsNewDlq }
            .Select(topic => new TopicSpecification
            {
                Name = topic,
                NumPartitions = _errorLogOptions.PartitionCount,
                ReplicationFactor = rf,
                Configs = new Dictionary<string, string>
                {
                    ["min.insync.replicas"] = minIsr.ToString(),
                    ["retention.ms"] = _errorLogOptions.RetentionMs.ToString()
                }
            })
            .ToArray();

        await IdempotentTopicProvisioner.ProvisionAsync(admin, specs, "DLQ", _logger, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

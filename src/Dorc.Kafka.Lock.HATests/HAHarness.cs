using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Lock.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Lock.HATests;

/// <summary>
/// Spins up N in-process KafkaLockCoordinator "candidates" sharing a lock
/// topic + consumer group. Mirrors the S-005a POC scenario drivers but wires
/// through the production KafkaDistributedLockService surface so the HA
/// suite exercises the same code path Monitor uses at runtime.
/// </summary>
internal sealed class HAHarness : IAsyncDisposable
{
    private readonly List<(KafkaLockCoordinator Coord, KafkaDistributedLockService Svc)> _candidates = new();
    private readonly string _topic;
    private readonly string _groupId;
    private readonly int _partitionCount;

    public HAHarness(string topic, string groupId, int partitionCount)
    {
        _topic = topic;
        _groupId = groupId;
        _partitionCount = partitionCount;
    }

    public async Task EnsureTopicAsync()
    {
        var adminConfig = new AdminClientConfig { BootstrapServers = HATestPrereq.Bootstrap };
        if (HATestPrereq.SaslUser is not null)
        {
            adminConfig.SecurityProtocol = SecurityProtocol.SaslSsl;
            adminConfig.SaslMechanism = SaslMechanism.ScramSha256;
            adminConfig.SaslUsername = HATestPrereq.SaslUser;
            adminConfig.SaslPassword = HATestPrereq.SaslPass;
            if (!string.IsNullOrWhiteSpace(HATestPrereq.SaslCa)) adminConfig.SslCaLocation = HATestPrereq.SaslCa;
        }
        using var admin = new AdminClientBuilder(adminConfig).Build();
        try
        {
            await admin.CreateTopicsAsync(new[] { new TopicSpecification
            {
                Name = _topic, NumPartitions = _partitionCount, ReplicationFactor = 1
            }});
        }
        catch (CreateTopicsException ex) when (ex.Results[0].Error.Code == ErrorCode.TopicAlreadyExists) { }
    }

    public async Task<(KafkaLockCoordinator Coord, KafkaDistributedLockService Svc)> AddCandidateAsync()
    {
        var clientOpts = Options.Create(new KafkaClientOptions
        {
            BootstrapServers = HATestPrereq.Bootstrap,
            ConsumerGroupId = _groupId,
            AuthMode = HATestPrereq.SaslUser is null ? KafkaAuthMode.Plaintext : KafkaAuthMode.SaslSsl,
            Sasl = new KafkaSaslOptions
            {
                Mechanism = "SCRAM-SHA-256",
                Username = HATestPrereq.SaslUser ?? "",
                Password = HATestPrereq.SaslPass ?? ""
            },
            SslCaLocation = HATestPrereq.SaslCa ?? "",
            SessionTimeoutMs = 10_000,
            HeartbeatIntervalMs = 3_000,
            MaxPollIntervalMs = 60_000
        });

        var lockOpts = Options.Create(new KafkaLocksOptions
        {
            Enabled = true,
            PartitionCount = _partitionCount,
            ReplicationFactor = 1,
            ConsumerGroupId = _groupId,
            LockWaitDefaultTimeoutMs = 15_000
        });

        var topicsOpts = Options.Create(new KafkaTopicsOptions
        {
            Locks = _topic
        });

        var coord = new KafkaLockCoordinator(
            new KafkaConnectionProvider(clientOpts),
            lockOpts,
            topicsOpts,
            NullLogger<KafkaLockCoordinator>.Instance);
        await coord.StartAsync(CancellationToken.None);
        var svc = new KafkaDistributedLockService(coord, lockOpts, NullLogger<KafkaDistributedLockService>.Instance);
        _candidates.Add((coord, svc));
        return (coord, svc);
    }

    public IReadOnlyList<(KafkaLockCoordinator Coord, KafkaDistributedLockService Svc)> Candidates => _candidates;

    public async Task RemoveCandidateAsync(KafkaLockCoordinator coord)
    {
        var idx = _candidates.FindIndex(c => ReferenceEquals(c.Coord, coord));
        if (idx < 0) return;
        await _candidates[idx].Coord.DisposeAsync();
        _candidates.RemoveAt(idx);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _candidates)
        {
            try { await c.Coord.DisposeAsync(); } catch { /* best-effort */ }
        }
        _candidates.Clear();
    }
}

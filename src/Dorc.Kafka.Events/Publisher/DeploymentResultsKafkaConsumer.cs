using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Consumers;
using Dorc.Kafka.Client.Serialization;
using Dorc.Kafka.ErrorLog;
using Dorc.PersistentData.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// API-side hosted service that subscribes to <c>dorc.results.status</c>,
/// deserialises Avro via S-003's factory, and rebroadcasts via the
/// injected <see cref="IDeploymentResultBroadcaster"/> (production: SignalR).
///
/// Per SPEC-S-007 R-2 multi-replica fan-out: consumer group id is
/// <c>dorc-api-results-status.{HostInstanceId}</c> so every API replica
/// consumes every event and broadcasts to its locally-pinned SignalR
/// clients. AutoOffsetReset overridden to Latest because status events
/// are real-time signals — a UI doesn't need historical replay on
/// consumer restart.
///
/// Failure path per R-3: poison messages and broadcast exceptions write a
/// KafkaErrorLogEntry via S-004's IKafkaErrorLog; if the DAL itself
/// throws, fall back to a structured LogError; super-degraded (logger
/// throws too) is swallowed so the consumer loop never crashes.
/// Offset commits only after the log path completes.
/// </summary>
public sealed class DeploymentResultsKafkaConsumer : BackgroundService
{
    public const string ConsumerGroupPrefix = "dorc-api-results-status";

    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly IKafkaSerializerFactory _serializerFactory;
    private readonly IDeploymentResultBroadcaster _broadcaster;
    private readonly IKafkaErrorLog _errorLog;
    private readonly KafkaErrorLogOptions _errorLogOptions;
    private readonly ILogger<DeploymentResultsKafkaConsumer> _logger;

    public DeploymentResultsKafkaConsumer(
        IKafkaConnectionProvider connectionProvider,
        IKafkaSerializerFactory serializerFactory,
        IDeploymentResultBroadcaster broadcaster,
        IKafkaErrorLog errorLog,
        IOptions<KafkaErrorLogOptions> errorLogOptions,
        ILogger<DeploymentResultsKafkaConsumer> logger)
    {
        _connectionProvider = connectionProvider;
        _serializerFactory = serializerFactory;
        _broadcaster = broadcaster;
        _errorLog = errorLog;
        _errorLogOptions = errorLogOptions.Value;
        _logger = logger;
    }

    public string ConsumerGroupId { get; init; } = $"{ConsumerGroupPrefix}.{HostInstanceId.Value}";

    /// <summary>
    /// The topic the consumer subscribes to. Defaults to the production
    /// <see cref="TopicName"/>; tests can
    /// override via object initializer to subscribe to a unique topic for
    /// isolation.
    /// </summary>
    public string TopicName { get; init; } = KafkaSubjectNames.ResultsStatusTopic;

    public int InsertTimeoutMs { get; init; } = 5_000;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Run(() => RunLoop(stoppingToken), stoppingToken);

    private void RunLoop(CancellationToken stoppingToken)
    {
        using var consumer = BuildConsumer();
        consumer.Subscribe(TopicName);
        _logger.LogInformation(
            "Kafka results-status consumer subscribed: topic={Topic} group={GroupId}",
            TopicName, ConsumerGroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, DeploymentResultEventData>? result = null;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                HandleFailure(consumer, ex.ConsumerRecord, ex, stoppingToken);
                // On pure transport-failure ConsumeException (no ConsumerRecord),
                // back off briefly so a dead broker doesn't busy-spin the loop
                // and flood the error-log DAL.
                if (ex.ConsumerRecord is null)
                {
                    try { Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).Wait(stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
                continue;
            }

            if (result is null) continue;

            try
            {
                _broadcaster.BroadcastAsync(result.Message.Value, stoppingToken)
                    .GetAwaiter().GetResult();
                _logger.LogInformation(
                    "broadcast-ok topic={Topic} partition={Partition} offset={Offset} group={GroupId} requestId={RequestId}",
                    result.Topic, result.Partition.Value, result.Offset.Value, ConsumerGroupId, result.Message.Value.RequestId);
                consumer.Commit(result);
            }
            catch (Exception ex)
            {
                HandleFailureFromConsumeResult(consumer, result, ex, stoppingToken);
            }
        }

        try { consumer.Close(); } catch { /* best-effort */ }
    }

    private IConsumer<string, DeploymentResultEventData> BuildConsumer()
    {
        // Use S-002's connection provider for SASL / bootstrap / timeouts,
        // but override AutoOffsetReset to Latest (status events are
        // real-time; no historical replay) and group.id to the per-replica
        // identity per R-2.
        var config = _connectionProvider.GetConsumerConfig(ConsumerGroupId);
        config.AutoOffsetReset = AutoOffsetReset.Latest;

        var handlers = new KafkaRebalanceHandlers<string, DeploymentResultEventData>(_logger, ConsumerGroupId);
        var builder = new ConsumerBuilder<string, DeploymentResultEventData>(config)
            .SetErrorHandler(handlers.OnError)
            .SetStatisticsHandler(handlers.OnStatistics)
            .SetPartitionsAssignedHandler(handlers.OnPartitionsAssigned)
            .SetPartitionsRevokedHandler(handlers.OnPartitionsRevoked)
            .SetPartitionsLostHandler(handlers.OnPartitionsLost);

        var valueDeserializer = _serializerFactory.GetValueDeserializer<DeploymentResultEventData>();
        if (valueDeserializer is not null) builder.SetValueDeserializer(valueDeserializer);

        return builder.Build();
    }

    private void HandleFailure(
        IConsumer<string, DeploymentResultEventData> consumer,
        ConsumeResult<byte[], byte[]>? rawRecord,
        Exception failure,
        CancellationToken stoppingToken)
    {
        var entry = new KafkaErrorLogEntry
        {
            Topic = rawRecord?.Topic ?? TopicName,
            Partition = rawRecord?.Partition.Value ?? -1,
            Offset = rawRecord?.Offset.Value ?? -1,
            ConsumerGroup = ConsumerGroupId,
            MessageKey = rawRecord?.Message?.Key is byte[] kb ? System.Text.Encoding.UTF8.GetString(kb) : null,
            RawPayload = rawRecord?.Message?.Value,
            Error = failure.Message,
            Stack = failure.StackTrace,
            OccurredAt = DateTimeOffset.UtcNow
        };
        WriteErrorLogAndCommit(consumer, entry, advanceOffset: rawRecord is not null, stoppingToken);
    }

    private void HandleFailureFromConsumeResult(
        IConsumer<string, DeploymentResultEventData> consumer,
        ConsumeResult<string, DeploymentResultEventData> result,
        Exception failure,
        CancellationToken stoppingToken)
    {
        var entry = new KafkaErrorLogEntry
        {
            Topic = result.Topic,
            Partition = result.Partition.Value,
            Offset = result.Offset.Value,
            ConsumerGroup = ConsumerGroupId,
            MessageKey = result.Message.Key,
            RawPayload = null,
            Error = failure.Message,
            Stack = failure.StackTrace,
            OccurredAt = DateTimeOffset.UtcNow
        };
        WriteErrorLogAndCommit(consumer, entry, advanceOffset: true, stoppingToken);
    }

    private void WriteErrorLogAndCommit(
        IConsumer<string, DeploymentResultEventData> consumer,
        KafkaErrorLogEntry entry,
        bool advanceOffset,
        CancellationToken stoppingToken)
    {
        try
        {
            using var insertCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            insertCts.CancelAfter(TimeSpan.FromMilliseconds(InsertTimeoutMs));
            _errorLog.InsertAsync(entry, insertCts.Token).GetAwaiter().GetResult();
            _logger.LogWarning(
                "error-logged topic={Topic} partition={Partition} offset={Offset} group={GroupId} error={Error}",
                entry.Topic, entry.Partition, entry.Offset, entry.ConsumerGroup, entry.Error);
        }
        catch (Exception dalEx)
        {
            try
            {
                _logger.LogError(dalEx,
                    "error-fallback-structured-log topic={Topic} partition={Partition} offset={Offset} group={GroupId} key={Key} error={Error}",
                    entry.Topic, entry.Partition, entry.Offset, entry.ConsumerGroup, entry.MessageKey, entry.Error);
            }
            catch
            {
                // Super-degraded: logger itself threw. Swallow so the consumer
                // loop survives — one missed log entry beats a halted consumer
                // that takes down further status updates for every connected
                // user. Per SPEC-S-007 R-3 #4.
            }
        }

        if (!advanceOffset) return;

        try
        {
            consumer.Commit();
        }
        catch (Exception commitEx)
        {
            _logger.LogError(commitEx,
                "Kafka offset commit failed after error-log path for topic={Topic} partition={Partition} offset={Offset}",
                entry.Topic, entry.Partition, entry.Offset);
        }
    }
}

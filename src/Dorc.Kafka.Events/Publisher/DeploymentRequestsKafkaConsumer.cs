using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Consumers;
using Dorc.Kafka.Client.Serialization;
using Dorc.Kafka.ErrorLog;
using Dorc.PersistentData.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// SPEC-S-006 R-3 Monitor-side hosted service. Subscribes to BOTH
/// <c>dorc.requests.new</c> and <c>dorc.requests.status</c>, deserialises Avro
/// via S-003's factory, and invokes <see cref="IRequestEventHandler"/> per
/// record. The handler's only effect is to raise the poll-loop wake-up
/// signal — this consumer NEVER executes requests; that remains the
/// existing DB-poll path's responsibility (S-005b's env-lock continues to
/// guard mutual exclusion).
///
/// Consumer group id: per-replica <c>dorc-monitor-requests.{HostInstanceId}</c>
/// so every Monitor replica sees every event (fan-out, mirroring S-007).
/// AutoOffsetReset overridden to Earliest to narrow the visibility gap on
/// consumer restart; rebalance-replay is harmless because handler is
/// idempotent + DB-state guards downstream paths.
///
/// Failure path mirrors S-007's results consumer: deserialise failure or
/// handler exception → S-004 IKafkaErrorLog DAL → fall back to structured
/// LogError → super-degraded swallow. The consumer loop never crashes on a
/// single record; AutoCommit advances the offset past the poison record.
/// </summary>
public sealed class DeploymentRequestsKafkaConsumer : BackgroundService
{
    public const string ConsumerGroupPrefix = "dorc-monitor-requests";

    private readonly IKafkaConnectionProvider _connectionProvider;
    private readonly IKafkaSerializerFactory _serializerFactory;
    private readonly IRequestEventHandler _handler;
    private readonly IKafkaErrorLog _errorLog;
    private readonly ILogger<DeploymentRequestsKafkaConsumer> _logger;

    public DeploymentRequestsKafkaConsumer(
        IKafkaConnectionProvider connectionProvider,
        IKafkaSerializerFactory serializerFactory,
        IRequestEventHandler handler,
        IKafkaErrorLog errorLog,
        ILogger<DeploymentRequestsKafkaConsumer> logger)
    {
        _connectionProvider = connectionProvider;
        _serializerFactory = serializerFactory;
        _handler = handler;
        _errorLog = errorLog;
        _logger = logger;
    }

    public string ConsumerGroupId { get; init; } = $"{ConsumerGroupPrefix}.{HostInstanceId.Value}";

    public string[] Topics { get; init; } = new[]
    {
        KafkaSubjectNames.RequestsNewTopic,
        KafkaSubjectNames.RequestsStatusTopic
    };

    public int InsertTimeoutMs { get; init; } = 5_000;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Run(() => RunLoop(stoppingToken), stoppingToken);

    private void RunLoop(CancellationToken stoppingToken)
    {
        using var consumer = BuildConsumer();
        consumer.Subscribe(Topics);
        _logger.LogInformation(
            "Kafka requests consumer subscribed: topics=[{Topics}] group={GroupId}",
            string.Join(",", Topics), ConsumerGroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, DeploymentRequestEventData>? result = null;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (ConsumeException ex)
            {
                HandleFailure(ex.ConsumerRecord, ex);
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
                _handler.HandleAsync(result.Topic, result.Message.Value, stoppingToken)
                    .GetAwaiter().GetResult();
                _logger.LogInformation(
                    "request-event-consumed topic={Topic} partition={Partition} offset={Offset} group={GroupId} requestId={RequestId} status={Status}",
                    result.Topic, result.Partition.Value, result.Offset.Value, ConsumerGroupId,
                    result.Message.Value.RequestId, result.Message.Value.Status);
            }
            catch (Exception ex)
            {
                HandleFailureFromConsumeResult(result, ex);
            }
        }

        try { consumer.Close(); } catch { /* best-effort */ }
    }

    private IConsumer<string, DeploymentRequestEventData> BuildConsumer()
    {
        var config = _connectionProvider.GetConsumerConfig(ConsumerGroupId);
        config.AutoOffsetReset = AutoOffsetReset.Earliest;
        config.EnableAutoCommit = true;

        var handlers = new KafkaRebalanceHandlers<string, DeploymentRequestEventData>(_logger, ConsumerGroupId);
        var builder = new ConsumerBuilder<string, DeploymentRequestEventData>(config)
            .SetErrorHandler(handlers.OnError)
            .SetStatisticsHandler(handlers.OnStatistics)
            .SetPartitionsAssignedHandler(handlers.OnPartitionsAssigned)
            .SetPartitionsRevokedHandler(handlers.OnPartitionsRevoked)
            .SetPartitionsLostHandler(handlers.OnPartitionsLost);

        var valueDeserializer = _serializerFactory.GetValueDeserializer<DeploymentRequestEventData>();
        if (valueDeserializer is not null) builder.SetValueDeserializer(valueDeserializer);

        return builder.Build();
    }

    private void HandleFailure(ConsumeResult<byte[], byte[]>? rawRecord, Exception failure)
    {
        var entry = new KafkaErrorLogEntry
        {
            Topic = rawRecord?.Topic ?? "",
            Partition = rawRecord?.Partition.Value ?? -1,
            Offset = rawRecord?.Offset.Value ?? -1,
            ConsumerGroup = ConsumerGroupId,
            MessageKey = rawRecord?.Message?.Key is byte[] kb ? System.Text.Encoding.UTF8.GetString(kb) : null,
            RawPayload = rawRecord?.Message?.Value,
            Error = failure.Message,
            Stack = failure.StackTrace,
            OccurredAt = DateTimeOffset.UtcNow
        };
        WriteErrorLog(entry);
    }

    private void HandleFailureFromConsumeResult(ConsumeResult<string, DeploymentRequestEventData> result, Exception failure)
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
        WriteErrorLog(entry);
    }

    private void WriteErrorLog(KafkaErrorLogEntry entry)
    {
        try
        {
            using var insertCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(InsertTimeoutMs));
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
                // Super-degraded: logger itself threw. Per SPEC-S-006 R-8 (mirrors
                // S-007 R-3 #4) — swallow so the consumer loop survives a single
                // bad record instead of crashing the whole acceleration layer.
            }
        }
    }
}

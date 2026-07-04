using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Kafka.Client;
using Dorc.Kafka.Events.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Kafka-substrate implementation of <see cref="IDeploymentEventsPublisher"/>.
///
/// Publish contract (none of the methods ever throw non-critical exceptions:
/// every call site publishes fire-and-forget, so a thrown exception would
/// land in an unobserved Task — failures are logged and, where one exists,
/// routed to the degraded path instead):
///
/// <para><b>Request-lifecycle events</b> (new / status) dual-publish:
/// SignalR fan-out via the injected
/// <see cref="IFallbackDeploymentEventPublisher"/> is attempted first
/// (best-effort UI continuity, WARN on failure), then the Kafka produce.
/// A Kafka produce failure is logged at ERROR and swallowed — the
/// Monitor's DB-poll baseline guarantees the request is still picked up.</para>
///
/// <para><b>Result-status events</b> publish to Kafka FIRST and only fall
/// back to direct SignalR fan-out when the produce fails. In the healthy
/// path delivery to UI clients happens exactly once, via the API-side
/// <see cref="DeploymentResultsKafkaConsumer"/> projection; publishing to
/// SignalR here as well would deliver every event at least twice (N+1
/// times with N API replicas on a SignalR backplane).</para>
/// </summary>
public sealed class KafkaDeploymentEventPublisher : IDeploymentEventsPublisher, IDisposable
{
    private readonly IProducer<string, DeploymentResultEventData> _resultsProducer;
    private readonly IProducer<string, DeploymentRequestEventData> _requestsProducer;
    private readonly IFallbackDeploymentEventPublisher _fallback;
    private readonly KafkaTopicsOptions _topics;
    private readonly ILogger<KafkaDeploymentEventPublisher> _logger;
    private int _disposed;

    public KafkaDeploymentEventPublisher(
        IProducer<string, DeploymentResultEventData> resultsProducer,
        IProducer<string, DeploymentRequestEventData> requestsProducer,
        IFallbackDeploymentEventPublisher fallback,
        IOptions<KafkaTopicsOptions> topics,
        ILogger<KafkaDeploymentEventPublisher> logger)
    {
        _resultsProducer = resultsProducer;
        _requestsProducer = requestsProducer;
        _fallback = fallback;
        _topics = topics.Value;
        _logger = logger;
    }

    public Task PublishNewRequestAsync(DeploymentRequestEventData eventData)
        => DualPublishRequestAsync(eventData, _topics.RequestsNew, kind: "new",
            () => _fallback.PublishNewRequestAsync(eventData));

    public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData)
        => DualPublishRequestAsync(eventData, _topics.RequestsStatus, kind: "status",
            () => _fallback.PublishRequestStatusChangedAsync(eventData));

    public async Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData)
    {
        var key = eventData.RequestId.ToString();
        try
        {
            var result = await _resultsProducer.ProduceAsync(
                _topics.ResultsStatus,
                new Message<string, DeploymentResultEventData> { Key = key, Value = eventData });
            _logger.LogDebug(
                "publish-ok topic={Topic} partition={Partition} offset={Offset} requestId={RequestId}",
                result.Topic, result.Partition.Value, result.Offset.Value, eventData.RequestId);
            // Healthy path ends here: the API-side results consumer projects
            // this event to SignalR. Broadcasting from here too would deliver
            // it to every client a second time.
            return;
        }
        catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
        {
            LogProduceFailure(ex, _topics.ResultsStatus, eventData.RequestId);
        }

        // Degraded path: Kafka produce failed, so the consumer projection
        // will never see this event. Direct SignalR fan-out keeps the UI
        // updated; best-effort because there is nothing further to fall
        // back to.
        try { await _fallback.PublishResultStatusChangedAsync(eventData); }
        catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
        {
            _logger.LogWarning(ex,
                "signalr-fallback-failed kind=result requestId={RequestId} resultId={ResultId}",
                eventData.RequestId, eventData.ResultId);
        }
    }

    private async Task DualPublishRequestAsync(
        DeploymentRequestEventData eventData,
        string topic,
        string kind,
        Func<Task> signalRAttempt)
    {
        try { await signalRAttempt(); }
        // SignalR fan-out is best-effort by design; catch broadly so any
        // hub-side or transport exception is logged WARN without
        // suppressing the Kafka emit. Process-fatal exceptions still
        // propagate via CriticalExceptions.IsCritical.
        catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
        {
            _logger.LogWarning(ex,
                "signalr-fanout-failed kind={Kind} requestId={RequestId}", kind, eventData.RequestId);
        }

        var key = eventData.RequestId.ToString();
        try
        {
            var result = await _requestsProducer.ProduceAsync(
                topic,
                new Message<string, DeploymentRequestEventData> { Key = key, Value = eventData });
            _logger.LogDebug(
                "publish-ok topic={Topic} partition={Partition} offset={Offset} requestId={RequestId} status={Status}",
                result.Topic, result.Partition.Value, result.Offset.Value, eventData.RequestId, eventData.Status);
        }
        // Swallowed, not rethrown: every caller publishes fire-and-forget,
        // so a throw here becomes an unobserved-task exception rather than
        // anyone's error handling. The Monitor's DB-poll baseline still
        // picks the request up; the Kafka emit is the acceleration path.
        catch (Exception ex) when (!CriticalExceptions.IsCritical(ex))
        {
            LogProduceFailure(ex, topic, eventData.RequestId);
        }
    }

    private void LogProduceFailure(Exception ex, string topic, int requestId)
    {
        var (reason, code) = ex switch
        {
            ProduceException<string, DeploymentResultEventData> pr => (pr.Error.Reason, pr.Error.Code.ToString()),
            ProduceException<string, DeploymentRequestEventData> pq => (pq.Error.Reason, pq.Error.Code.ToString()),
            KafkaException k => (k.Error.Reason, k.Error.Code.ToString()),
            _ => (ex.Message, ex.GetType().Name)
        };
        _logger.LogError(ex,
            "publish-failed topic={Topic} requestId={RequestId} reason={Reason} code={Code}",
            topic, requestId, reason, code);
    }

    public void Dispose()
    {
        // Idempotent: the container can track this instance through more
        // than one registration (concrete singleton + interface forward),
        // and a disposed librdkafka producer throws on every member.
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        try { _resultsProducer.Flush(TimeSpan.FromSeconds(2)); }
        catch (Exception ex) when (ex is KafkaException or ObjectDisposedException) { /* best-effort */ }
        try { _resultsProducer.Dispose(); }
        finally
        {
            try { _requestsProducer.Flush(TimeSpan.FromSeconds(2)); }
            catch (Exception ex) when (ex is KafkaException or ObjectDisposedException) { /* best-effort */ }
            _requestsProducer.Dispose();
        }
    }
}

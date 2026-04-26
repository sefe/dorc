using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Kafka-substrate implementation of <see cref="IDeploymentEventsPublisher"/>.
///
/// All three methods follow the SPEC-S-006 R-1 / R-2 dual-publish ordering
/// invariant: SignalR fan-out (via the injected
/// <see cref="IFallbackDeploymentEventPublisher"/>) is attempted FIRST,
/// then the Kafka produce. Kafka failure throws after the SignalR attempt
/// completes — never suppressed by it. SignalR failure is logged at
/// WARN and never suppresses the Kafka emit. This preserves the
/// acceleration-layer framing: DB is authoritative, Kafka is the
/// authoritative event substrate, SignalR is best-effort UI fan-out.
///
/// Result-status method (S-007) and the two request-lifecycle methods
/// (S-006) all share the same pattern; the only differences are topic
/// name, value type, and producer instance.
/// </summary>
public sealed class KafkaDeploymentEventPublisher : IDeploymentEventsPublisher, IDisposable
{
    private readonly IProducer<string, DeploymentResultEventData> _resultsProducer;
    private readonly IProducer<string, DeploymentRequestEventData> _requestsProducer;
    private readonly IFallbackDeploymentEventPublisher _fallback;
    private readonly ILogger<KafkaDeploymentEventPublisher> _logger;

    public KafkaDeploymentEventPublisher(
        IProducer<string, DeploymentResultEventData> resultsProducer,
        IProducer<string, DeploymentRequestEventData> requestsProducer,
        IFallbackDeploymentEventPublisher fallback,
        ILogger<KafkaDeploymentEventPublisher> logger)
    {
        _resultsProducer = resultsProducer;
        _requestsProducer = requestsProducer;
        _fallback = fallback;
        _logger = logger;
    }

    public Task PublishNewRequestAsync(DeploymentRequestEventData eventData)
        => DualPublishRequestAsync(eventData, KafkaSubjectNames.RequestsNewTopic, kind: "new",
            () => _fallback.PublishNewRequestAsync(eventData));

    public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData)
        => DualPublishRequestAsync(eventData, KafkaSubjectNames.RequestsStatusTopic, kind: "status",
            () => _fallback.PublishRequestStatusChangedAsync(eventData));

    public async Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData)
    {
        // SignalR fan-out first per S-006 R-1 ordering invariant.
        try { await _fallback.PublishResultStatusChangedAsync(eventData); }
        // SignalR is best-effort UI fan-out (class doc, R-1). The fallback
        // implementation can surface HubException, IOException,
        // TimeoutException, InvalidOperationException, or
        // ObjectDisposedException depending on hub state — narrowing to any
        // single type would let the others escape the publisher and break
        // the "WARN and continue to Kafka emit" contract.
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "signalr-fanout-failed kind=result requestId={RequestId} resultId={ResultId}",
                eventData.RequestId, eventData.ResultId);
        }

        var key = eventData.RequestId.ToString();
        try
        {
            var result = await _resultsProducer.ProduceAsync(
                KafkaSubjectNames.ResultsStatusTopic,
                new Message<string, DeploymentResultEventData> { Key = key, Value = eventData });
            _logger.LogDebug(
                "publish-ok topic={Topic} partition={Partition} offset={Offset} requestId={RequestId}",
                result.Topic, result.Partition.Value, result.Offset.Value, eventData.RequestId);
        }
        catch (ProduceException<string, DeploymentResultEventData> ex)
        {
            _logger.LogError(ex,
                "publish-failed topic={Topic} requestId={RequestId} reason={Reason} code={Code}",
                KafkaSubjectNames.ResultsStatusTopic, eventData.RequestId, ex.Error.Reason, ex.Error.Code);
            throw;
        }
    }

    private async Task DualPublishRequestAsync(
        DeploymentRequestEventData eventData,
        string topic,
        string kind,
        Func<Task> signalRAttempt)
    {
        try { await signalRAttempt(); }
        // Mirror PublishResultStatusChangedAsync — SignalR fan-out is
        // best-effort by design; catch broadly so any hub-side or transport
        // exception is logged WARN without suppressing the Kafka emit.
        catch (Exception ex)
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
        catch (ProduceException<string, DeploymentRequestEventData> ex)
        {
            _logger.LogError(ex,
                "publish-failed topic={Topic} requestId={RequestId} reason={Reason} code={Code}",
                topic, eventData.RequestId, ex.Error.Reason, ex.Error.Code);
            throw;
        }
    }

    public void Dispose()
    {
        try { _resultsProducer.Flush(TimeSpan.FromSeconds(2)); } catch { /* best-effort */ }
        try { _requestsProducer.Flush(TimeSpan.FromSeconds(2)); } catch { /* best-effort */ }
        _resultsProducer.Dispose();
        _requestsProducer.Dispose();
    }
}

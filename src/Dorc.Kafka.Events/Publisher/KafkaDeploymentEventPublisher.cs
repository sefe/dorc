using Confluent.Kafka;
using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Events.Publisher;

/// <summary>
/// Kafka-substrate implementation of <see cref="IDeploymentEventsPublisher"/>.
/// Per SPEC-S-007 R-1: only <see cref="PublishResultStatusChangedAsync"/>
/// produces to Kafka; the two request-lifecycle methods delegate to the
/// injected <see cref="IFallbackDeploymentEventPublisher"/> so they keep
/// flowing through SignalR until S-006 lands.
///
/// When S-006 lands it replaces the two delegated method bodies with Kafka
/// produces against <c>dorc.requests.new</c> / <c>dorc.requests.status</c>.
/// The class and its DI registration remain; the delegation-constructor
/// parameter becomes dead and is removed.
/// </summary>
public sealed class KafkaDeploymentEventPublisher : IDeploymentEventsPublisher, IDisposable
{
    private readonly IProducer<string, DeploymentResultEventData> _resultsProducer;
    private readonly IFallbackDeploymentEventPublisher _fallback;
    private readonly ILogger<KafkaDeploymentEventPublisher> _logger;

    public KafkaDeploymentEventPublisher(
        IProducer<string, DeploymentResultEventData> resultsProducer,
        IFallbackDeploymentEventPublisher fallback,
        ILogger<KafkaDeploymentEventPublisher> logger)
    {
        _resultsProducer = resultsProducer;
        _fallback = fallback;
        _logger = logger;
    }

    public Task PublishNewRequestAsync(DeploymentRequestEventData eventData)
        => _fallback.PublishNewRequestAsync(eventData);

    public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData)
        => _fallback.PublishRequestStatusChangedAsync(eventData);

    public async Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData)
    {
        var key = eventData.RequestId.ToString();
        try
        {
            var result = await _resultsProducer.ProduceAsync(
                KafkaSubjectNames.ResultsStatusTopic,
                new Message<string, DeploymentResultEventData>
                {
                    Key = key,
                    Value = eventData
                });
            _logger.LogDebug(
                "Kafka publish ok: topic={Topic} partition={Partition} offset={Offset} requestId={RequestId}",
                result.Topic, result.Partition.Value, result.Offset.Value, eventData.RequestId);
        }
        catch (ProduceException<string, DeploymentResultEventData> ex)
        {
            _logger.LogError(ex,
                "Kafka publish failed: topic={Topic} requestId={RequestId} reason={Reason} code={Code}",
                KafkaSubjectNames.ResultsStatusTopic, eventData.RequestId, ex.Error.Reason, ex.Error.Code);
            throw;
        }
    }

    public void Dispose()
    {
        try { _resultsProducer.Flush(TimeSpan.FromSeconds(2)); } catch { /* best-effort */ }
        _resultsProducer.Dispose();
    }
}

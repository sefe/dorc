using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Kafka.Events;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dorc.Kafka.Events.Tests.Publisher;

[TestClass]
public class KafkaDeploymentEventPublisherTests
{
    // AT-1 per SPEC-S-007 §4.

    [TestMethod]
    public async Task PublishResultStatus_ProducesOneMessageToResultsStatusTopic_KeyedByRequestId()
    {
        var producer = new StubProducer<string, DeploymentResultEventData>();
        var fallback = new RecordingFallback();
        var sut = new KafkaDeploymentEventPublisher(producer, fallback, NullLogger<KafkaDeploymentEventPublisher>.Instance);

        var eventData = new DeploymentResultEventData(
            ResultId: 1, RequestId: 4242, ComponentId: 7,
            Status: "Running",
            StartedTime: DateTimeOffset.UtcNow, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow);

        await sut.PublishResultStatusChangedAsync(eventData);

        Assert.AreEqual(1, producer.Produced.Count);
        var (topic, message) = producer.Produced[0];
        Assert.AreEqual(KafkaSubjectNames.ResultsStatusTopic, topic);
        Assert.AreEqual("4242", message.Key);
        Assert.IsNotNull(message.Value);
        Assert.AreEqual(eventData, message.Value);
    }

    [TestMethod]
    public async Task PublishNewRequest_DelegatesToFallback_DoesNotTouchProducer()
    {
        var producer = new StubProducer<string, DeploymentResultEventData>();
        var fallback = new RecordingFallback();
        var sut = new KafkaDeploymentEventPublisher(producer, fallback, NullLogger<KafkaDeploymentEventPublisher>.Instance);

        var eventData = new DeploymentRequestEventData(
            RequestId: 1, Status: "New",
            StartedTime: DateTimeOffset.UtcNow, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow);

        await sut.PublishNewRequestAsync(eventData);

        Assert.AreEqual(0, producer.Produced.Count);
        Assert.AreEqual(1, fallback.NewRequestCalls.Count);
        Assert.AreEqual(eventData, fallback.NewRequestCalls[0]);
    }

    [TestMethod]
    public async Task PublishRequestStatusChanged_DelegatesToFallback_DoesNotTouchProducer()
    {
        var producer = new StubProducer<string, DeploymentResultEventData>();
        var fallback = new RecordingFallback();
        var sut = new KafkaDeploymentEventPublisher(producer, fallback, NullLogger<KafkaDeploymentEventPublisher>.Instance);

        var eventData = new DeploymentRequestEventData(
            RequestId: 2, Status: "Completed",
            StartedTime: DateTimeOffset.UtcNow, CompletedTime: DateTimeOffset.UtcNow, Timestamp: DateTimeOffset.UtcNow);

        await sut.PublishRequestStatusChangedAsync(eventData);

        Assert.AreEqual(0, producer.Produced.Count);
        Assert.AreEqual(1, fallback.RequestStatusCalls.Count);
        Assert.AreEqual(eventData, fallback.RequestStatusCalls[0]);
    }

    [TestMethod]
    public async Task PublishResultStatus_NegativeRequestId_StillProducesWithStringKey()
    {
        // Edge: ensure RequestId.ToString() survives odd values (negative,
        // zero). Key routing is the contract — not value validation.
        var producer = new StubProducer<string, DeploymentResultEventData>();
        var sut = new KafkaDeploymentEventPublisher(producer, new RecordingFallback(), NullLogger<KafkaDeploymentEventPublisher>.Instance);

        await sut.PublishResultStatusChangedAsync(new DeploymentResultEventData(
            ResultId: 0, RequestId: -1, ComponentId: 0,
            Status: null, StartedTime: null, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow));

        Assert.AreEqual("-1", producer.Produced.Single().Message.Key);
    }

    private sealed class RecordingFallback : IFallbackDeploymentEventPublisher
    {
        public List<DeploymentRequestEventData> NewRequestCalls { get; } = new();
        public List<DeploymentRequestEventData> RequestStatusCalls { get; } = new();
        public List<DeploymentResultEventData> ResultStatusCalls { get; } = new();

        public Task PublishNewRequestAsync(DeploymentRequestEventData eventData)
        {
            NewRequestCalls.Add(eventData);
            return Task.CompletedTask;
        }

        public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData)
        {
            RequestStatusCalls.Add(eventData);
            return Task.CompletedTask;
        }

        public Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData)
        {
            ResultStatusCalls.Add(eventData);
            return Task.CompletedTask;
        }
    }
}

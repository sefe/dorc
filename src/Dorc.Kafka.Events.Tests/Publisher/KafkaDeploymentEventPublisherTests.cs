using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Tests.Publisher;

[TestClass]
public class KafkaDeploymentEventPublisherTests
{
    private static readonly KafkaTopicsOptions TestTopics = new();

    private static IOptions<KafkaTopicsOptions> Topics() => Options.Create(TestTopics);

    private static (KafkaDeploymentEventPublisher sut,
                    StubProducer<string, DeploymentResultEventData> resultsProd,
                    StubProducer<string, DeploymentRequestEventData> requestsProd,
                    RecordingFallback fallback) Build()
    {
        var resultsProd = new StubProducer<string, DeploymentResultEventData>();
        var requestsProd = new StubProducer<string, DeploymentRequestEventData>();
        var fallback = new RecordingFallback();
        var sut = new KafkaDeploymentEventPublisher(
            resultsProd, requestsProd, fallback, Topics(),
            NullLogger<KafkaDeploymentEventPublisher>.Instance);
        return (sut, resultsProd, requestsProd, fallback);
    }

    private static DeploymentRequestEventData NewRequest(int requestId, string status) => new(
        RequestId: requestId, Status: status,
        StartedTime: DateTimeOffset.UtcNow, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow);

    // AT-1 — producer emits to correct topic with RequestId key.
    [TestMethod]
    public async Task PublishNewRequest_EmitsToRequestsNewTopic_KeyedByRequestId()
    {
        var (sut, _, requestsProd, _) = Build();
        var ev = NewRequest(4242, "Pending");

        await sut.PublishNewRequestAsync(ev);

        Assert.AreEqual(1, requestsProd.Produced.Count);
        Assert.AreEqual(TestTopics.RequestsNew, requestsProd.Produced[0].Topic);
        Assert.AreEqual("4242", requestsProd.Produced[0].Message.Key);
        Assert.AreEqual(ev, requestsProd.Produced[0].Message.Value);
    }

    [TestMethod]
    public async Task PublishRequestStatusChanged_EmitsToRequestsStatusTopic_KeyedByRequestId()
    {
        var (sut, _, requestsProd, _) = Build();
        var ev = NewRequest(99, "Cancelled");

        await sut.PublishRequestStatusChangedAsync(ev);

        Assert.AreEqual(1, requestsProd.Produced.Count);
        Assert.AreEqual(TestTopics.RequestsStatus, requestsProd.Produced[0].Topic);
        Assert.AreEqual("99", requestsProd.Produced[0].Message.Key);
    }

    [TestMethod]
    public async Task PublishResultStatus_EmitsToResultsStatusTopic_KeyedByRequestId()
    {
        var (sut, resultsProd, _, _) = Build();
        var ev = new DeploymentResultEventData(
            ResultId: 1, RequestId: 4242, ComponentId: 7, Status: "Running",
            StartedTime: DateTimeOffset.UtcNow, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow);

        await sut.PublishResultStatusChangedAsync(ev);

        Assert.AreEqual(1, resultsProd.Produced.Count);
        Assert.AreEqual(TestTopics.ResultsStatus, resultsProd.Produced[0].Topic);
        Assert.AreEqual("4242", resultsProd.Produced[0].Message.Key);
    }

    // AT-2 — dual-publish: SignalR called regardless of Kafka outcome; Kafka throw propagates.
    [TestMethod]
    public async Task PublishNewRequest_AlsoCallsFallback_BeforeKafka()
    {
        var (sut, _, _, fallback) = Build();
        var ev = NewRequest(1, "Pending");

        await sut.PublishNewRequestAsync(ev);

        Assert.AreEqual(1, fallback.NewRequestCalls.Count, "SignalR fallback must be invoked.");
    }

    [TestMethod]
    public async Task PublishNewRequest_KafkaThrows_StillCallsFallbackFirst_AndPropagates()
    {
        var resultsProd = new StubProducer<string, DeploymentResultEventData>();
        var requestsProd = new StubProducer<string, DeploymentRequestEventData> { ThrowOnProduce = true };
        var fallback = new RecordingFallback();
        var sut = new KafkaDeploymentEventPublisher(resultsProd, requestsProd, fallback, Topics(), NullLogger<KafkaDeploymentEventPublisher>.Instance);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await sut.PublishNewRequestAsync(NewRequest(7, "Pending")));

        Assert.AreEqual(1, fallback.NewRequestCalls.Count,
            "SignalR emit must be attempted BEFORE Kafka, and not suppressed by Kafka throw.");
    }

    [TestMethod]
    public async Task PublishNewRequest_FallbackThrows_DoesNotSuppressKafkaEmit()
    {
        var resultsProd = new StubProducer<string, DeploymentResultEventData>();
        var requestsProd = new StubProducer<string, DeploymentRequestEventData>();
        var fallback = new RecordingFallback { ThrowOnNew = true };
        var sut = new KafkaDeploymentEventPublisher(resultsProd, requestsProd, fallback, Topics(), NullLogger<KafkaDeploymentEventPublisher>.Instance);

        await sut.PublishNewRequestAsync(NewRequest(11, "Pending"));

        Assert.AreEqual(1, requestsProd.Produced.Count,
            "Kafka emit must NOT be suppressed by SignalR fallback failure (R-1 invariant).");
    }

    [TestMethod]
    public async Task PublishResultStatus_FallbackThrows_DoesNotSuppressKafkaEmit()
    {
        var resultsProd = new StubProducer<string, DeploymentResultEventData>();
        var requestsProd = new StubProducer<string, DeploymentRequestEventData>();
        var fallback = new RecordingFallback { ThrowOnResult = true };
        var sut = new KafkaDeploymentEventPublisher(resultsProd, requestsProd, fallback, Topics(), NullLogger<KafkaDeploymentEventPublisher>.Instance);

        await sut.PublishResultStatusChangedAsync(new DeploymentResultEventData(
            ResultId: 1, RequestId: 1, ComponentId: 1, Status: "Running",
            StartedTime: null, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow));

        Assert.AreEqual(1, resultsProd.Produced.Count);
    }

    private sealed class RecordingFallback : IFallbackDeploymentEventPublisher
    {
        public List<DeploymentRequestEventData> NewRequestCalls { get; } = new();
        public List<DeploymentRequestEventData> RequestStatusCalls { get; } = new();
        public List<DeploymentResultEventData> ResultStatusCalls { get; } = new();
        public bool ThrowOnNew { get; set; }
        public bool ThrowOnRequestStatus { get; set; }
        public bool ThrowOnResult { get; set; }

        public Task PublishNewRequestAsync(DeploymentRequestEventData eventData)
        {
            NewRequestCalls.Add(eventData);
            if (ThrowOnNew) throw new InvalidOperationException("signalr-down");
            return Task.CompletedTask;
        }

        public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData)
        {
            RequestStatusCalls.Add(eventData);
            if (ThrowOnRequestStatus) throw new InvalidOperationException("signalr-down");
            return Task.CompletedTask;
        }

        public Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData)
        {
            ResultStatusCalls.Add(eventData);
            if (ThrowOnResult) throw new InvalidOperationException("signalr-down");
            return Task.CompletedTask;
        }
    }
}

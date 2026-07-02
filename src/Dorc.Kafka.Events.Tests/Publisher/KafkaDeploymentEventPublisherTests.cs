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

    // — producer emits to correct topic with RequestId key.
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

    // — request-lifecycle dual-publish: SignalR called regardless of Kafka
    // outcome; Kafka produce failure is logged and swallowed (every call
    // site is fire-and-forget, so a throw would be an unobserved-task
    // exception nobody handles).
    [TestMethod]
    public async Task PublishNewRequest_AlsoCallsFallback_BeforeKafka()
    {
        var (sut, _, _, fallback) = Build();
        var ev = NewRequest(1, "Pending");

        await sut.PublishNewRequestAsync(ev);

        Assert.AreEqual(1, fallback.NewRequestCalls.Count, "SignalR fallback must be invoked.");
    }

    [TestMethod]
    public async Task PublishNewRequest_KafkaThrows_StillCallsFallback_AndDoesNotThrow()
    {
        var resultsProd = new StubProducer<string, DeploymentResultEventData>();
        var requestsProd = new StubProducer<string, DeploymentRequestEventData> { ThrowOnProduce = true };
        var fallback = new RecordingFallback();
        var sut = new KafkaDeploymentEventPublisher(resultsProd, requestsProd, fallback, Topics(), NullLogger<KafkaDeploymentEventPublisher>.Instance);

        // Must not throw: callers publish fire-and-forget.
        await sut.PublishNewRequestAsync(NewRequest(7, "Pending"));

        Assert.AreEqual(1, fallback.NewRequestCalls.Count,
            "SignalR emit must be attempted BEFORE Kafka, and not suppressed by Kafka failure.");
    }

    [TestMethod]
    public async Task PublishRequestStatusChanged_KafkaThrows_DoesNotThrow()
    {
        var resultsProd = new StubProducer<string, DeploymentResultEventData>();
        var requestsProd = new StubProducer<string, DeploymentRequestEventData> { ThrowOnProduce = true };
        var fallback = new RecordingFallback();
        var sut = new KafkaDeploymentEventPublisher(resultsProd, requestsProd, fallback, Topics(), NullLogger<KafkaDeploymentEventPublisher>.Instance);

        await sut.PublishRequestStatusChangedAsync(NewRequest(8, "Running"));

        Assert.AreEqual(1, fallback.RequestStatusCalls.Count);
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
            "Kafka emit must NOT be suppressed by SignalR fallback failure.");
    }

    // — result-status is Kafka-first with SignalR strictly as the degraded
    // path: in the healthy path the API-side projection consumer is the
    // ONLY SignalR delivery, otherwise every client receives each event
    // at least twice (N+1 times with N API replicas on a backplane).
    [TestMethod]
    public async Task PublishResultStatus_KafkaSucceeds_DoesNotCallFallback()
    {
        var (sut, resultsProd, _, fallback) = Build();

        await sut.PublishResultStatusChangedAsync(new DeploymentResultEventData(
            ResultId: 1, RequestId: 1, ComponentId: 1, Status: "Running",
            StartedTime: null, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow));

        Assert.AreEqual(1, resultsProd.Produced.Count);
        Assert.AreEqual(0, fallback.ResultStatusCalls.Count,
            "Healthy path must NOT fan out to SignalR here — the results consumer projection is the single delivery.");
    }

    [TestMethod]
    public async Task PublishResultStatus_KafkaThrows_FallsBackToSignalR_AndDoesNotThrow()
    {
        var resultsProd = new StubProducer<string, DeploymentResultEventData> { ThrowOnProduce = true };
        var requestsProd = new StubProducer<string, DeploymentRequestEventData>();
        var fallback = new RecordingFallback();
        var sut = new KafkaDeploymentEventPublisher(resultsProd, requestsProd, fallback, Topics(), NullLogger<KafkaDeploymentEventPublisher>.Instance);

        await sut.PublishResultStatusChangedAsync(new DeploymentResultEventData(
            ResultId: 2, RequestId: 2, ComponentId: 1, Status: "Failed",
            StartedTime: null, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow));

        Assert.AreEqual(1, fallback.ResultStatusCalls.Count,
            "Kafka produce failure must trigger the direct SignalR fallback so the UI still updates.");
    }

    [TestMethod]
    public async Task PublishResultStatus_KafkaAndFallbackThrow_DoesNotThrow()
    {
        var resultsProd = new StubProducer<string, DeploymentResultEventData> { ThrowOnProduce = true };
        var requestsProd = new StubProducer<string, DeploymentRequestEventData>();
        var fallback = new RecordingFallback { ThrowOnResult = true };
        var sut = new KafkaDeploymentEventPublisher(resultsProd, requestsProd, fallback, Topics(), NullLogger<KafkaDeploymentEventPublisher>.Instance);

        // Fully degraded: both legs fail; the publisher still must not throw.
        await sut.PublishResultStatusChangedAsync(new DeploymentResultEventData(
            ResultId: 3, RequestId: 3, ComponentId: 1, Status: "Failed",
            StartedTime: null, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var resultsProd = new StubProducer<string, DeploymentResultEventData>();
        var requestsProd = new StubProducer<string, DeploymentRequestEventData>();
        var sut = new KafkaDeploymentEventPublisher(
            resultsProd, requestsProd, new RecordingFallback(), Topics(),
            NullLogger<KafkaDeploymentEventPublisher>.Instance);

        // The container can track the instance through both the concrete
        // singleton registration and the interface forward, so it may be
        // disposed more than once.
        sut.Dispose();
        sut.Dispose();

        Assert.AreEqual(1, resultsProd.DisposeCount);
        Assert.AreEqual(1, requestsProd.DisposeCount);
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

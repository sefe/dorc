using Dorc.Core.Events;
using Dorc.Core.Interfaces;
using Dorc.Kafka.Client.DependencyInjection;
using Dorc.Kafka.Events.DependencyInjection;
using Dorc.Kafka.Events.Publisher;
using Dorc.Kafka.ErrorLog;
using Dorc.PersistentData.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dorc.Kafka.Events.Tests.DependencyInjection;

[TestClass]
public class KafkaResultsStatusSubstrateExtensionsTests
{
    // : substrate-selector flag is gone. Kafka is unconditional.
    // The remaining DI invariants worth pinning are: (a) Kafka publisher +
    // hosted services are registered; (b) re-calling the extension is a no-op.

    [TestMethod]
    public void Registration_RegistersPublisherAndHostedServices()
    {
        var services = BaseServices();
        services.AddSingleton<Confluent.Kafka.IProducer<string, DeploymentResultEventData>>(
            new Dorc.Kafka.Events.Tests.Publisher.StubProducer<string, DeploymentResultEventData>());
        services.AddSingleton<Confluent.Kafka.IProducer<string, DeploymentRequestEventData>>(
            new Dorc.Kafka.Events.Tests.Publisher.StubProducer<string, DeploymentRequestEventData>());
        services.AddSingleton<IDeploymentResultBroadcaster>(new NoopBroadcaster());
        services.AddSingleton<IKafkaErrorLog>(new NoopErrorLog());
        services.AddOptions<KafkaErrorLogOptions>();
        services.AddSingleton<IFallbackDeploymentEventPublisher>(SentinelDirectPublisher.Instance);
        services.AddSingleton<IDeploymentEventsPublisher>(SentinelDirectPublisher.Instance);
        services.AddDorcKafkaClient(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kafka:BootstrapServers"] = "localhost:9092" })
            .Build());

        services.AddDorcKafkaResultsStatusSubstrate(EmptyConfig());

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IDeploymentEventsPublisher>();
        Assert.IsInstanceOfType<KafkaDeploymentEventPublisher>(publisher);

        var hosted = sp.GetServices<IHostedService>().ToList();
        Assert.IsTrue(hosted.OfType<DeploymentResultsKafkaConsumer>().Any());
        Assert.IsTrue(hosted.OfType<KafkaResultsStatusTopicProvisioner>().Any());
    }

    // Regression: the IDeploymentEventsPublisher forward was once registered
    // Scoped. A scoped factory registration makes every resolving scope track
    // the returned singleton in its disposables, so the first disposed
    // request scope disposed the shared Kafka producers and every later
    // publish threw ObjectDisposedException.
    [TestMethod]
    public void PublisherForward_IsSingleton()
    {
        var services = BaseServices();
        services.AddSingleton<IFallbackDeploymentEventPublisher>(SentinelDirectPublisher.Instance);
        services.AddDorcKafkaClient(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kafka:BootstrapServers"] = "localhost:9092" })
            .Build());

        services.AddDorcKafkaResultsStatusSubstrate(EmptyConfig());

        var descriptor = services.Last(sd => sd.ServiceType == typeof(IDeploymentEventsPublisher));
        Assert.AreEqual(ServiceLifetime.Singleton, descriptor.Lifetime,
            "A scoped forward would let request scopes dispose the singleton publisher's producers.");
    }

    [TestMethod]
    public async Task DisposingResolutionScope_DoesNotDisposeSingletonPublisher()
    {
        var services = BaseServices();
        var resultsProducer = new Dorc.Kafka.Events.Tests.Publisher.StubProducer<string, DeploymentResultEventData>();
        var requestsProducer = new Dorc.Kafka.Events.Tests.Publisher.StubProducer<string, DeploymentRequestEventData>();
        services.AddSingleton<Confluent.Kafka.IProducer<string, DeploymentResultEventData>>(resultsProducer);
        services.AddSingleton<Confluent.Kafka.IProducer<string, DeploymentRequestEventData>>(requestsProducer);
        services.AddSingleton<IDeploymentResultBroadcaster>(new NoopBroadcaster());
        services.AddSingleton<IKafkaErrorLog>(new NoopErrorLog());
        services.AddOptions<KafkaErrorLogOptions>();
        services.AddSingleton<IFallbackDeploymentEventPublisher>(SentinelDirectPublisher.Instance);
        services.AddSingleton<IDeploymentEventsPublisher>(SentinelDirectPublisher.Instance);
        services.AddDorcKafkaClient(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kafka:BootstrapServers"] = "localhost:9092" })
            .Build());
        services.AddDorcKafkaResultsStatusSubstrate(EmptyConfig());

        // ValidateScopes mirrors the Development-environment provider the
        // API builds; the old wiring failed validation outright.
        await using var sp = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        using (var scope = sp.CreateScope())
        {
            // Simulates a controller resolving the publisher per request.
            _ = scope.ServiceProvider.GetRequiredService<IDeploymentEventsPublisher>();
        }

        Assert.AreEqual(0, resultsProducer.DisposeCount,
            "Disposing a request scope must not dispose the singleton publisher's producers.");

        // The substrate must still be able to publish after a scope died.
        var publisher = sp.GetRequiredService<IDeploymentEventsPublisher>();
        await publisher.PublishResultStatusChangedAsync(new DeploymentResultEventData(
            ResultId: 1, RequestId: 1, ComponentId: 1, Status: "Running",
            StartedTime: null, CompletedTime: null, Timestamp: DateTimeOffset.UtcNow));
        Assert.AreEqual(1, resultsProducer.Produced.Count);
    }

    [TestMethod]
    public void CalledTwice_IsIdempotent()
    {
        var services = BaseServices();
        services.AddSingleton<IFallbackDeploymentEventPublisher>(SentinelDirectPublisher.Instance);
        services.AddDorcKafkaClient(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kafka:BootstrapServers"] = "localhost:9092" })
            .Build());
        services.AddDorcKafkaResultsStatusSubstrate(EmptyConfig());
        var countAfterFirst = services.Count;

        services.AddDorcKafkaResultsStatusSubstrate(EmptyConfig());
        var countAfterSecond = services.Count;

        Assert.AreEqual(countAfterFirst, countAfterSecond);
    }

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    private sealed class NoopBroadcaster : IDeploymentResultBroadcaster
    {
        public Task BroadcastAsync(DeploymentResultEventData eventData, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopErrorLog : IKafkaErrorLog
    {
        public Task InsertAsync(KafkaErrorLogEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<KafkaErrorLogEntry>> QueryAsync(string? topic, string? consumerGroup, DateTimeOffset? sinceUtc, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<KafkaErrorLogEntry>>(Array.Empty<KafkaErrorLogEntry>());
        public Task<int> PurgeAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class SentinelDirectPublisher : IDeploymentEventsPublisher, IFallbackDeploymentEventPublisher
    {
        public static readonly SentinelDirectPublisher Instance = new();
        public Task PublishNewRequestAsync(DeploymentRequestEventData eventData) => Task.CompletedTask;
        public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData) => Task.CompletedTask;
        public Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData) => Task.CompletedTask;
    }
}

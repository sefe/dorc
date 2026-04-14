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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Tests.DependencyInjection;

[TestClass]
public class KafkaResultsStatusSubstrateExtensionsTests
{
    // AT-7 + AT-8 per SPEC-S-007 §4.

    [TestMethod]
    public void DirectMode_DoesNotRegisterKafkaArtefacts_BackCompat()
    {
        var services = BaseServices();
        RegisterUpstream(services, KafkaSubstrateMode.Direct);

        var sp = services.BuildServiceProvider();

        // No Kafka publisher resolved.
        var publisher = sp.GetRequiredService<IDeploymentEventsPublisher>();
        Assert.IsNotInstanceOfType<KafkaDeploymentEventPublisher>(publisher);
        Assert.AreSame(SentinelDirectPublisher.Instance, publisher);

        // No hosted services.
        var hosted = sp.GetServices<IHostedService>().ToList();
        Assert.IsFalse(hosted.OfType<DeploymentResultsKafkaConsumer>().Any());
        Assert.IsFalse(hosted.OfType<KafkaResultsStatusTopicProvisioner>().Any());
    }

    [TestMethod]
    public void KafkaMode_ReplacesPublisherAndRegistersHostedServices()
    {
        var services = BaseServices();
        // Pre-register a stub IProducer so the extension's TryAdd path
        // doesn't trigger the real builder (which would need a working
        // Avro serializer factory for DeploymentResultEventData). We're
        // proving DI wiring, not end-to-end produce — that's AT-2's job.
        services.AddSingleton<Confluent.Kafka.IProducer<string, DeploymentResultEventData>>(
            new Dorc.Kafka.Events.Tests.Publisher.StubProducer<string, DeploymentResultEventData>());
        services.AddSingleton<IDeploymentResultBroadcaster>(new NoopBroadcaster());
        services.AddSingleton<IKafkaErrorLog>(new NoopErrorLog());
        services.AddOptions<KafkaErrorLogOptions>();
        RegisterUpstream(services, KafkaSubstrateMode.Kafka);

        var sp = services.BuildServiceProvider();

        var publisher = sp.GetRequiredService<IDeploymentEventsPublisher>();
        Assert.IsInstanceOfType<KafkaDeploymentEventPublisher>(publisher);

        var hosted = sp.GetServices<IHostedService>().ToList();
        Assert.IsTrue(hosted.OfType<DeploymentResultsKafkaConsumer>().Any());
        Assert.IsTrue(hosted.OfType<KafkaResultsStatusTopicProvisioner>().Any());
    }

    [TestMethod]
    public void CalledTwice_IsIdempotent()
    {
        var services = BaseServices();
        RegisterUpstream(services, KafkaSubstrateMode.Kafka);
        var countAfterFirst = services.Count;

        services.AddDorcKafkaResultsStatusSubstrate(BuildConfig(KafkaSubstrateMode.Kafka));
        var countAfterSecond = services.Count;

        Assert.AreEqual(countAfterFirst, countAfterSecond);
    }

    [TestMethod]
    public void InvalidSubstrateValue_FailsAtExtensionRegistration()
    {
        var services = BaseServices();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:Substrate:ResultsStatus"] = "Bogus"
            })
            .Build();

        // Per SPEC-S-007 R-7: invalid substrate string fails at host build
        // with the failing key named. .NET's config binder silently falls
        // back on invalid enum strings; the extension checks explicitly.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => services.AddDorcKafkaResultsStatusSubstrate(config));
        StringAssert.Contains(ex.Message, nameof(KafkaSubstrateOptions.ResultsStatus));
        StringAssert.Contains(ex.Message, "Bogus");
    }

    [TestMethod]
    public void DefaultConfig_ResolvesAsDirectMode()
    {
        var services = BaseServices();
        // No upstream IDeploymentEventsPublisher registration yet — the
        // extension on Direct path doesn't register one.
        services.AddSingleton<IDeploymentEventsPublisher>(SentinelDirectPublisher.Instance);
        services.AddDorcKafkaResultsStatusSubstrate(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IDeploymentEventsPublisher>();

        Assert.AreSame(SentinelDirectPublisher.Instance, publisher);
    }

    private static void RegisterUpstream(ServiceCollection services, KafkaSubstrateMode mode)
    {
        var config = BuildConfig(mode);
        // Upstream (typical Program.cs): register the direct publisher as
        // both IDeploymentEventsPublisher AND IFallbackDeploymentEventPublisher.
        services.AddSingleton<IDeploymentEventsPublisher>(SentinelDirectPublisher.Instance);
        services.AddSingleton<IFallbackDeploymentEventPublisher>(SentinelDirectPublisher.Instance);
        // Upstream DI for the Kafka client layer (so IKafkaProducerBuilder /
        // serializer factory resolve if Kafka mode activates).
        services.AddDorcKafkaClient(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "localhost:9092"
            })
            .Build());

        services.AddDorcKafkaResultsStatusSubstrate(config);
    }

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    private static IConfiguration BuildConfig(KafkaSubstrateMode mode)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:Substrate:ResultsStatus"] = mode.ToString()
            })
            .Build();

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

using Dorc.Core.Events;
using Dorc.Kafka.Events.DependencyInjection;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dorc.Kafka.Events.Tests.DependencyInjection;

/// <summary>
/// the substrate-selector flag is gone; Kafka is unconditional.
/// Remaining DI invariants: latching signal + handler + consumer registered;
/// idempotency holds; the extension does NOT touch the publisher interfaces.
/// </summary>
[TestClass]
public class KafkaRequestLifecycleSubstrateExtensionsTests
{
    private static IConfiguration EmptyConfig()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    private static ServiceCollection BaseServices()
    {
        var s = new ServiceCollection();
        s.AddLogging();
        return s;
    }

    [TestMethod]
    public void RegistersLatchingSignal_HandlerAndConsumer()
    {
        var services = BaseServices();
        services.AddDorcKafkaRequestLifecycleSubstrate(EmptyConfig());

        using var sp = services.BuildServiceProvider();
        Assert.IsInstanceOfType(sp.GetRequiredService<IRequestPollSignal>(), typeof(RequestPollSignal));
        Assert.IsInstanceOfType(sp.GetRequiredService<IRequestEventHandler>(), typeof(PollSignalRequestEventHandler));
        Assert.IsTrue(services.Any(sd => sd.ServiceType == typeof(DeploymentRequestsKafkaConsumer)));
    }

    [TestMethod]
    public void DoesNotRegisterPublisherInterfaces()
    {
        // guard: this extension must NOT register
        // IDeploymentEventsPublisher or IFallbackDeploymentEventPublisher
        // both stay owned by AddDorcKafkaResultsStatusSubstrate.
        var services = BaseServices();
        services.AddDorcKafkaRequestLifecycleSubstrate(EmptyConfig());
        Assert.IsFalse(services.Any(sd => sd.ServiceType == typeof(Dorc.Core.Interfaces.IDeploymentEventsPublisher)));
        Assert.IsFalse(services.Any(sd => sd.ServiceType == typeof(Dorc.Core.Interfaces.IFallbackDeploymentEventPublisher)));
    }

    [TestMethod]
    public void Idempotent_SecondCallIsNoOp()
    {
        var services = BaseServices();
        services.AddDorcKafkaRequestLifecycleSubstrate(EmptyConfig());
        var firstCount = services.Count;
        services.AddDorcKafkaRequestLifecycleSubstrate(EmptyConfig());
        Assert.AreEqual(firstCount, services.Count);
    }

    // The Monitor consumes dorc.requests.* but used to rely on the API host
    // to provision them — a Monitor-first start on a fresh cluster consumed
    // nonexistent topics in an error loop. Hosted services start in
    // registration order, so the provisioner must precede the consumer.
    [TestMethod]
    public void RegistersTopicProvisioner_BeforeConsumer()
    {
        var services = BaseServices();
        services.AddDorcKafkaRequestLifecycleSubstrate(EmptyConfig());

        var hosted = services
            .Where(sd => sd.ServiceType == typeof(IHostedService))
            .ToList();
        var provisionerIndex = hosted.FindIndex(sd =>
            sd.ImplementationType == typeof(KafkaResultsStatusTopicProvisioner));
        var consumerIndex = hosted.FindIndex(sd =>
            sd.ImplementationType is null && sd.ImplementationFactory is not null);

        Assert.IsTrue(provisionerIndex >= 0, "Request-topic provisioner must be registered by this extension.");
        Assert.IsTrue(consumerIndex >= 0, "Requests consumer hosted service must be registered.");
        Assert.IsTrue(provisionerIndex < consumerIndex,
            "Provisioner must start before the consumer subscribes.");
    }

    [TestMethod]
    public void DoesNotDuplicateProvisioner_WhenResultsSubstrateAlreadyRegisteredIt()
    {
        var services = BaseServices();
        services.AddSingleton<Dorc.Core.Interfaces.IFallbackDeploymentEventPublisher>(
            new NoopFallback());
        services.AddDorcKafkaResultsStatusSubstrate(EmptyConfig());
        services.AddDorcKafkaRequestLifecycleSubstrate(EmptyConfig());

        var provisionerCount = services.Count(sd =>
            sd.ServiceType == typeof(IHostedService)
            && sd.ImplementationType == typeof(KafkaResultsStatusTopicProvisioner));
        Assert.AreEqual(1, provisionerCount);
    }

    private sealed class NoopFallback : Dorc.Core.Interfaces.IFallbackDeploymentEventPublisher
    {
        public Task PublishNewRequestAsync(DeploymentRequestEventData eventData) => Task.CompletedTask;
        public Task PublishRequestStatusChangedAsync(DeploymentRequestEventData eventData) => Task.CompletedTask;
        public Task PublishResultStatusChangedAsync(DeploymentResultEventData eventData) => Task.CompletedTask;
    }
}

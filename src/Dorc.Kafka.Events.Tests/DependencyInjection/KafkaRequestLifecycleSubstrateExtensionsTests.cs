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
}

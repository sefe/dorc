using Dorc.Core.Events;
using Dorc.Kafka.Events.DependencyInjection;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dorc.Kafka.Events.Tests.DependencyInjection;

/// <summary>AT-6 + AT-7 — substrate flag switches behaviour; invalid enum throws.</summary>
[TestClass]
public class KafkaRequestLifecycleSubstrateExtensionsTests
{
    private static IConfiguration Cfg(string? mode)
    {
        var dict = new Dictionary<string, string?>();
        if (mode is not null) dict["Kafka:Substrate:RequestLifecycle"] = mode;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static ServiceCollection BaseServices()
    {
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IRequestPollSignal, NoOpRequestPollSignal>();
        return s;
    }

    [TestMethod]
    public void Direct_LeavesUpstreamPollSignalUntouched_NoConsumerRegistered()
    {
        var services = BaseServices();
        services.AddDorcKafkaRequestLifecycleSubstrate(Cfg(null));

        using var sp = services.BuildServiceProvider();
        Assert.IsInstanceOfType(sp.GetRequiredService<IRequestPollSignal>(), typeof(NoOpRequestPollSignal));
        Assert.IsFalse(services.Any(sd => sd.ServiceType == typeof(DeploymentRequestsKafkaConsumer)));
    }

    [TestMethod]
    public void Kafka_ReplacesPollSignal_AndRegistersConsumer()
    {
        var services = BaseServices();
        services.AddDorcKafkaRequestLifecycleSubstrate(Cfg("Kafka"));

        using var sp = services.BuildServiceProvider();
        Assert.IsInstanceOfType(sp.GetRequiredService<IRequestPollSignal>(), typeof(RequestPollSignal));
        Assert.IsInstanceOfType(sp.GetRequiredService<IRequestEventHandler>(), typeof(PollSignalRequestEventHandler));
        Assert.IsTrue(services.Any(sd => sd.ServiceType == typeof(DeploymentRequestsKafkaConsumer)));
        // R-2 GPT-F7 guard: this extension must NOT register IDeploymentEventsPublisher
        // or IFallbackDeploymentEventPublisher (both are S-007's).
        Assert.IsFalse(services.Any(sd => sd.ServiceType == typeof(Dorc.Core.Interfaces.IDeploymentEventsPublisher)));
        Assert.IsFalse(services.Any(sd => sd.ServiceType == typeof(Dorc.Core.Interfaces.IFallbackDeploymentEventPublisher)));
    }

    [TestMethod]
    public void InvalidEnum_FailsFastAtRegistration()
    {
        var services = BaseServices();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            services.AddDorcKafkaRequestLifecycleSubstrate(Cfg("Bogus")));
    }

    [TestMethod]
    public void Idempotent_SecondCallIsNoOp()
    {
        var services = BaseServices();
        services.AddDorcKafkaRequestLifecycleSubstrate(Cfg("Kafka"));
        var firstCount = services.Count;
        services.AddDorcKafkaRequestLifecycleSubstrate(Cfg("Kafka"));
        Assert.AreEqual(firstCount, services.Count);
    }
}

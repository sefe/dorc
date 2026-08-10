using Confluent.Kafka;
using Dorc.Kafka.ErrorLog;
using Dorc.Kafka.ErrorLog.DependencyInjection;
using Dorc.Kafka.Events.Configuration;
using Dorc.Kafka.Events.Publisher;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dorc.Kafka.Events.Tests.DependencyInjection;

[TestClass]
public class KafkaErrorLogExtensionsTests
{
    private static IConfiguration Config(Dictionary<string, string?>? values = null)
        => new ConfigurationBuilder().AddInMemoryCollection(values ?? new Dictionary<string, string?>()).Build();

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    [TestMethod]
    public void Registration_ProvidesErrorLog_RoutedOnlyForRequestsNew()
    {
        var services = BaseServices();
        services.AddSingleton<IProducer<string, KafkaErrorEnvelope>>(
            new Dorc.Kafka.Events.Tests.Publisher.StubProducer<string, KafkaErrorEnvelope>());

        services.AddDorcKafkaErrorLog(Config());

        using var sp = services.BuildServiceProvider();
        var errorLog = sp.GetRequiredService<IKafkaErrorLog>();
        Assert.IsInstanceOfType<KafkaErrorLog>(errorLog);
    }

    [TestMethod]
    public void Registration_AddsDlqTopicProvisionerHostedService()
    {
        var services = BaseServices();
        services.AddDorcKafkaErrorLog(Config());

        var hosted = services
            .Where(sd => sd.ServiceType == typeof(IHostedService))
            .ToList();
        Assert.IsTrue(hosted.Any(sd => sd.ImplementationType == typeof(KafkaErrorLogDlqTopicProvisioner)),
            "DLQ topic provisioner must be registered as a hosted service.");
    }

    [TestMethod]
    public void Registration_BindsAndValidatesErrorLogOptions()
    {
        var services = BaseServices();
        services.AddDorcKafkaErrorLog(Config(new Dictionary<string, string?>
        {
            ["Kafka:ErrorLog:MaxPayloadBytes"] = "1234",
            ["Kafka:ErrorLog:ProduceTimeoutMs"] = "999",
            ["Kafka:ErrorLog:PartitionCount"] = "3",
            ["Kafka:ErrorLog:ReplicationFactor"] = "3",
            ["Kafka:ErrorLog:RetentionMs"] = "1000"
        }));

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KafkaErrorLogOptions>>().Value;
        Assert.AreEqual(1234, options.MaxPayloadBytes);
        Assert.AreEqual(999, options.ProduceTimeoutMs);
    }

    [TestMethod]
    public void CalledTwice_IsIdempotent()
    {
        var services = BaseServices();
        services.AddDorcKafkaErrorLog(Config());
        var first = services.Count;

        services.AddDorcKafkaErrorLog(Config());

        Assert.AreEqual(first, services.Count);
    }
}

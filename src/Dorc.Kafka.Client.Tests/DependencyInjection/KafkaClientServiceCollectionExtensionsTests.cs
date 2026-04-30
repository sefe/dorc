using Dorc.Kafka.Client.Configuration;
using Dorc.Kafka.Client.Connection;
using Dorc.Kafka.Client.Consumers;
using Dorc.Kafka.Client.DependencyInjection;
using Dorc.Kafka.Client.Producers;
using Dorc.Kafka.Client.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Client.Tests.DependencyInjection;

[TestClass]
public class KafkaClientServiceCollectionExtensionsTests
{
    [TestMethod]
    public void AddDorcKafkaClient_RegistersAllCoreServices_ResolvableViaDI()
    {
        var sp = BuildProvider(ValidConfig());

        Assert.IsNotNull(sp.GetRequiredService<IKafkaConnectionProvider>());
        Assert.IsNotNull(sp.GetRequiredService<IKafkaSerializerFactory>());
        Assert.IsNotNull(sp.GetRequiredService<IKafkaProducerBuilder<string, byte[]>>());
        Assert.IsNotNull(sp.GetRequiredService<IKafkaConsumerBuilder<string, byte[]>>());
        Assert.AreEqual("broker:9092", sp.GetRequiredService<IOptions<KafkaClientOptions>>().Value.BootstrapServers);
    }

    [TestMethod]
    public void AddDorcKafkaClient_CalledTwice_DoesNotDoubleRegister()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddLogging();

        services.AddDorcKafkaClient(ValidConfig());
        var countAfterFirst = services.Count;

        services.AddDorcKafkaClient(ValidConfig());
        var countAfterSecond = services.Count;

        Assert.AreEqual(countAfterFirst, countAfterSecond, "Second call must be idempotent.");
    }

    [TestMethod]
    public void AddDorcKafkaClient_ValidateOnStart_ThrowsWhenMandatoryKeyMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:AuthMode"] = "Plaintext"  // BootstrapServers deliberately missing
            })
            .Build();

        var sp = BuildProvider(config);

        var ex = Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = sp.GetRequiredService<IOptions<KafkaClientOptions>>().Value);
        StringAssert.Contains(ex.Message, nameof(KafkaClientOptions.BootstrapServers));
    }

    [TestMethod]
    public void AddDorcKafkaClient_ConsumerBuilderResolvedViaDI_CanBuildRealConsumer()
    {
        var sp = BuildProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "127.0.0.1:1",
                ["Kafka:ConsumerGroupId"] = "unit-test-group"
            })
            .Build());

        var builder = sp.GetRequiredService<IKafkaConsumerBuilder<string, byte[]>>();
        using var consumer = builder.Build("ut-consumer");

        Assert.IsNotNull(consumer);
    }

    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDorcKafkaClient(configuration);
        return services.BuildServiceProvider();
    }

    private static IConfiguration ValidConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "broker:9092"
        })
        .Build();

    private sealed class NullLoggerFactory : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public void Dispose() { }
    }
}

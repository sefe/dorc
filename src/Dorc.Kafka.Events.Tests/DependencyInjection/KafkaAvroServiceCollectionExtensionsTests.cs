using Confluent.SchemaRegistry;
using Dorc.Core.Events;
using Dorc.Kafka.Client.DependencyInjection;
using Dorc.Kafka.Client.Serialization;
using Dorc.Kafka.Events.DependencyInjection;
using Dorc.Kafka.Events.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Events.Tests.DependencyInjection;

[TestClass]
public class KafkaAvroServiceCollectionExtensionsTests
{
    [TestMethod]
    public void AddDorcKafkaAvro_AfterClient_ReplacesDefaultFactoryWithAvro()
    {
        var services = BuildBaseServices();
        services.AddDorcKafkaClient(ValidConfig());
        services.AddDorcKafkaAvro(ValidConfig());

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IKafkaSerializerFactory>();

        Assert.IsInstanceOfType<AvroKafkaSerializerFactory>(factory);
    }

    [TestMethod]
    public void AddDorcKafkaAvro_BeforeClient_StillEndsWithAvroFactoryActive()
    {
        // Spec R-4: extension must tolerate either call order.
        var services = BuildBaseServices();
        services.AddDorcKafkaAvro(ValidConfig());
        services.AddDorcKafkaClient(ValidConfig());

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IKafkaSerializerFactory>();

        Assert.IsInstanceOfType<AvroKafkaSerializerFactory>(factory);
    }

    [TestMethod]
    public void AddDorcKafkaAvro_BeforeClient_FactoryCanSerialiseInScopeType()
    {
        // Stronger variant of the above: prove the Avro factory is not just
        // registered first, but is actually the one resolved and usable.
        var services = BuildBaseServices();
        services.AddDorcKafkaAvro(ValidConfig());
        services.AddDorcKafkaClient(ValidConfig());

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IKafkaSerializerFactory>();

        Assert.IsNotNull(factory.GetValueSerializer<DeploymentRequestEventData>());
        Assert.IsNull(factory.GetValueSerializer<string>());
    }

    [TestMethod]
    public void AddDorcKafkaAvro_CalledTwice_IsIdempotent()
    {
        var services = BuildBaseServices();
        services.AddDorcKafkaClient(ValidConfig());

        services.AddDorcKafkaAvro(ValidConfig());
        var countAfterFirst = services.Count;

        services.AddDorcKafkaAvro(ValidConfig());
        var countAfterSecond = services.Count;

        Assert.AreEqual(countAfterFirst, countAfterSecond);
    }

    [TestMethod]
    public void AddDorcKafkaAvro_ResolvesSchemaRegistryClient_FromKafkaSchemaRegistryUrl()
    {
        var services = BuildBaseServices();
        services.AddDorcKafkaClient(ValidConfig());
        services.AddDorcKafkaAvro(ValidConfig());

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ISchemaRegistryClient>();

        Assert.IsNotNull(registry);
    }

    [TestMethod]
    public void AvroFactory_ReturnsNonNullSerializerForInScopeType()
    {
        var services = BuildBaseServices();
        services.AddDorcKafkaClient(ValidConfig());
        services.AddDorcKafkaAvro(ValidConfig());

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IKafkaSerializerFactory>();

        Assert.IsNotNull(factory.GetValueSerializer<DeploymentRequestEventData>());
        Assert.IsNotNull(factory.GetValueSerializer<DeploymentResultEventData>());
    }

    [TestMethod]
    public void AvroFactory_ReturnsNullForOutOfScopeType()
    {
        var services = BuildBaseServices();
        services.AddDorcKafkaClient(ValidConfig());
        services.AddDorcKafkaAvro(ValidConfig());

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IKafkaSerializerFactory>();

        Assert.IsNull(factory.GetValueSerializer<string>());
        Assert.IsNull(factory.GetValueSerializer<byte[]>());
    }

    [TestMethod]
    public void AvroFactory_InScopeTypes_ListsTheTwoEventContracts()
    {
        var services = BuildBaseServices();
        services.AddDorcKafkaClient(ValidConfig());
        services.AddDorcKafkaAvro(ValidConfig());

        var factory = (AvroKafkaSerializerFactory)services
            .BuildServiceProvider()
            .GetRequiredService<IKafkaSerializerFactory>();

        CollectionAssert.Contains(factory.InScopeTypes.ToList(), typeof(DeploymentRequestEventData));
        CollectionAssert.Contains(factory.InScopeTypes.ToList(), typeof(DeploymentResultEventData));
        Assert.AreEqual(2, factory.InScopeTypes.Count);
    }

    private static ServiceCollection BuildBaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        return services;
    }

    private static IConfiguration ValidConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "localhost:9092",
            ["Kafka:SchemaRegistry:Url"] = "http://localhost:8081"
        })
        .Build();

    private sealed class NullLoggerProvider : ILoggerProvider
    {
        public static readonly NullLoggerProvider Instance = new();
        public ILogger CreateLogger(string categoryName) => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public void Dispose() { }
    }
}

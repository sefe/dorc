using Chr.Avro.Confluent;
using Confluent.SchemaRegistry;
using Dorc.Kafka.Client.DependencyInjection;
using Dorc.Kafka.Client.Serialization;
using Dorc.Kafka.Events.DependencyInjection;
using Dorc.Kafka.Events.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Events.Tests.Serialization;

/// <summary>
/// Pins the KafkaAvroOptions.AllowAutomaticSchemaRegistration flow-through
/// to the Chr.Avro serializer registration behaviour. Default MUST be Never:
/// with Always, any producer holding registry write credentials registers an
/// evolved schema on first publish, silently bypassing the PR-time
/// AvroSchemaGate evolution-safety check.
/// </summary>
[TestClass]
public class AvroSchemaRegistrationBehaviorTests
{
    [TestMethod]
    public void Factory_DefaultOptions_RegistrationBehaviorIsNever()
    {
        var factory = new AvroKafkaSerializerFactory(NewRegistryClient(), Options.Create(new KafkaAvroOptions()));

        Assert.AreEqual(AutomaticRegistrationBehavior.Never, factory.RegistrationBehavior);
    }

    [TestMethod]
    public void Factory_NullOptions_RegistrationBehaviorIsNever()
    {
        var factory = new AvroKafkaSerializerFactory(NewRegistryClient(), options: null);

        Assert.AreEqual(AutomaticRegistrationBehavior.Never, factory.RegistrationBehavior);
    }

    [TestMethod]
    public void Factory_AllowAutomaticSchemaRegistration_RegistrationBehaviorIsAlways()
    {
        var factory = new AvroKafkaSerializerFactory(
            NewRegistryClient(),
            Options.Create(new KafkaAvroOptions { AllowAutomaticSchemaRegistration = true }));

        Assert.AreEqual(AutomaticRegistrationBehavior.Always, factory.RegistrationBehavior);
    }

    [TestMethod]
    public void DiResolvedFactory_FlagBoundFromConfiguration_FlowsThrough()
    {
        // End-to-end through the real registration path: appsettings key →
        // KafkaAvroOptions binding → factory ctor → registration behaviour.
        var factory = ResolveFactoryWith(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "localhost:9092",
            ["Kafka:SchemaRegistry:Url"] = "http://localhost:8081",
            ["Kafka:Avro:AllowAutomaticSchemaRegistration"] = "true"
        });

        Assert.AreEqual(AutomaticRegistrationBehavior.Always, factory.RegistrationBehavior);
    }

    [TestMethod]
    public void DiResolvedFactory_FlagAbsent_DefaultsToNever()
    {
        var factory = ResolveFactoryWith(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "localhost:9092",
            ["Kafka:SchemaRegistry:Url"] = "http://localhost:8081"
        });

        Assert.AreEqual(AutomaticRegistrationBehavior.Never, factory.RegistrationBehavior);
    }

    private static AvroKafkaSerializerFactory ResolveFactoryWith(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddDorcKafkaClient(config);
        services.AddDorcKafkaAvro(config);
        return (AvroKafkaSerializerFactory)services
            .BuildServiceProvider()
            .GetRequiredService<IKafkaSerializerFactory>();
    }

    private static ISchemaRegistryClient NewRegistryClient()
        => new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = "http://localhost:8081" });

    private sealed class NullLoggerProvider : ILoggerProvider
    {
        public static readonly NullLoggerProvider Instance = new();
        public ILogger CreateLogger(string categoryName) => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public void Dispose() { }
    }
}

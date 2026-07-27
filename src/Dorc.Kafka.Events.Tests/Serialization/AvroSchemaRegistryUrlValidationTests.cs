using Confluent.SchemaRegistry;
using Dorc.Kafka.Client.DependencyInjection;
using Dorc.Kafka.Events.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dorc.Kafka.Events.Tests.Serialization;

/// <summary>
/// A missing or EMPTY Kafka:SchemaRegistry:Url must fail loudly at
/// first resolution of the registry client, naming the config key — not
/// detonate later inside CachedSchemaRegistryClient with an opaque error.
/// Configuration binding yields "" (not null) for a present-but-blank key,
/// so a null-coalescing guard alone never fires; these tests pin the
/// IsNullOrWhiteSpace check in AddDorcKafkaAvro.
/// </summary>
[TestClass]
public class AvroSchemaRegistryUrlValidationTests
{
    [TestMethod]
    public void ResolveRegistryClient_EmptyUrl_ThrowsNamingTheConfigKey()
    {
        var sp = BuildProvider(schemaRegistryUrl: "");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => sp.GetRequiredService<ISchemaRegistryClient>());
        StringAssert.Contains(ex.Message, "Kafka:SchemaRegistry:Url");
    }

    [TestMethod]
    public void ResolveRegistryClient_WhitespaceUrl_ThrowsNamingTheConfigKey()
    {
        var sp = BuildProvider(schemaRegistryUrl: "   ");

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => sp.GetRequiredService<ISchemaRegistryClient>());
        StringAssert.Contains(ex.Message, "Kafka:SchemaRegistry:Url");
    }

    [TestMethod]
    public void ResolveRegistryClient_MissingUrl_ThrowsNamingTheConfigKey()
    {
        var sp = BuildProvider(schemaRegistryUrl: null);

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => sp.GetRequiredService<ISchemaRegistryClient>());
        StringAssert.Contains(ex.Message, "Kafka:SchemaRegistry:Url");
    }

    [TestMethod]
    public void ResolveRegistryClient_ValidUrl_Resolves()
    {
        var sp = BuildProvider(schemaRegistryUrl: "http://localhost:8081");

        Assert.IsNotNull(sp.GetRequiredService<ISchemaRegistryClient>());
    }

    private static ServiceProvider BuildProvider(string? schemaRegistryUrl)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "localhost:9092"
        };
        if (schemaRegistryUrl is not null)
            settings["Kafka:SchemaRegistry:Url"] = schemaRegistryUrl;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddDorcKafkaClient(config);
        services.AddDorcKafkaAvro(config);
        return services.BuildServiceProvider();
    }

    private sealed class NullLoggerProvider : ILoggerProvider
    {
        public static readonly NullLoggerProvider Instance = new();
        public ILogger CreateLogger(string categoryName) => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public void Dispose() { }
    }
}

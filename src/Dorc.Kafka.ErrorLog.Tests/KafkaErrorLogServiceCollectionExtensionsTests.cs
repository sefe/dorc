using Dorc.Kafka.ErrorLog.DependencyInjection;
using Dorc.PersistentData.Contexts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.ErrorLog.Tests;

[TestClass]
public class KafkaErrorLogServiceCollectionExtensionsTests
{
    [TestMethod]
    public void AddDorcKafkaErrorLog_ResolvesIKafkaErrorLog_AgainstHostWithContextFactory()
    {
        var sp = BuildHost(ValidConfig());

        var dal = sp.GetRequiredService<IKafkaErrorLog>();

        Assert.IsNotNull(dal);
    }

    [TestMethod]
    public void AddDorcKafkaErrorLog_CalledTwice_IsIdempotent()
    {
        var services = BaseServices();
        services.AddDorcKafkaErrorLog(ValidConfig());
        var countAfterFirst = services.Count;

        services.AddDorcKafkaErrorLog(ValidConfig());
        var countAfterSecond = services.Count;

        Assert.AreEqual(countAfterFirst, countAfterSecond);
    }

    [TestMethod]
    public void AddDorcKafkaErrorLog_BindsDefaults()
    {
        var sp = BuildHost(ValidConfig());

        var options = sp.GetRequiredService<IOptions<KafkaErrorLogOptions>>().Value;

        Assert.AreEqual(30, options.RetentionDays);
        Assert.AreEqual(65_536, options.MaxPayloadBytes);
        Assert.AreEqual(5_000, options.PurgeBatchSize);
    }

    [TestMethod]
    public void AddDorcKafkaErrorLog_BindsOverrides()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:ErrorLog:RetentionDays"] = "7",
                ["Kafka:ErrorLog:MaxPayloadBytes"] = "1024",
                ["Kafka:ErrorLog:PurgeBatchSize"] = "100"
            })
            .Build();

        var sp = BuildHost(config);
        var options = sp.GetRequiredService<IOptions<KafkaErrorLogOptions>>().Value;

        Assert.AreEqual(7, options.RetentionDays);
        Assert.AreEqual(1024, options.MaxPayloadBytes);
        Assert.AreEqual(100, options.PurgeBatchSize);
    }

    [TestMethod]
    [DataRow("Kafka:ErrorLog:RetentionDays", "0", "RetentionDays")]
    [DataRow("Kafka:ErrorLog:RetentionDays", "-1", "RetentionDays")]
    [DataRow("Kafka:ErrorLog:MaxPayloadBytes", "0", "MaxPayloadBytes")]
    [DataRow("Kafka:ErrorLog:PurgeBatchSize", "0", "PurgeBatchSize")]
    [DataRow("Kafka:ErrorLog:QueryMaxRowsCap", "0", "QueryMaxRowsCap")]
    public void AddDorcKafkaErrorLog_InvalidOption_FailsWithKeyInMessage(string overrideKey, string overrideValue, string expectedKey)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Kafka:ErrorLog:RetentionDays"] = "30",
            ["Kafka:ErrorLog:MaxPayloadBytes"] = "1024",
            ["Kafka:ErrorLog:PurgeBatchSize"] = "100",
            ["Kafka:ErrorLog:QueryMaxRowsCap"] = "10000",
            [overrideKey] = overrideValue
        };
        var sp = BuildHost(new ConfigurationBuilder().AddInMemoryCollection(dict).Build());

        var ex = Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = sp.GetRequiredService<IOptions<KafkaErrorLogOptions>>().Value);
        StringAssert.Contains(ex.Message, expectedKey);
    }

    private static ServiceProvider BuildHost(IConfiguration configuration)
    {
        var services = BaseServices();
        services.AddDorcKafkaErrorLog(configuration);
        return services.BuildServiceProvider();
    }

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Real DAL needs IKafkaErrorLogContextFactory; in this DI test we
        // never actually exercise the DB, so a no-op factory suffices.
        services.AddSingleton<IKafkaErrorLogContextFactory>(_ => new NoopContextFactory());
        services.AddSingleton<IDeploymentContextFactory>(_ => null!);
        return services;
    }

    private static IConfiguration ValidConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>())
        .Build();

    private sealed class NoopContextFactory : IKafkaErrorLogContextFactory
    {
        public IKafkaErrorLogContext GetContext() => throw new NotSupportedException();
    }
}

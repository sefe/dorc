using Dorc.Kafka.Client.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dorc.Kafka.Client.Tests.Configuration;

[TestClass]
public class KafkaClientOptionsBindingTests
{
    [TestMethod]
    public void Bind_PopulatesEveryR1Field_FromConfiguration()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "broker1:9092,broker2:9092",
            ["Kafka:AuthMode"] = "SaslSsl",
            ["Kafka:Sasl:Mechanism"] = "SCRAM-SHA-256",
            ["Kafka:Sasl:Username"] = "svc-dorc",
            ["Kafka:Sasl:Password"] = "hunter2",
            ["Kafka:SslCaLocation"] = "/etc/ssl/corp-root.pem",
            ["Kafka:SchemaRegistry:Url"] = "https://registry.example:8081",
            ["Kafka:SchemaRegistry:BasicAuthUsername"] = "sr-user",
            ["Kafka:SchemaRegistry:BasicAuthPassword"] = "sr-pass",
            ["Kafka:ConsumerGroupId"] = "dorc-monitor",
            ["Kafka:AutoOffsetReset"] = "Latest",
            ["Kafka:EnableAutoCommit"] = "true",
            ["Kafka:SessionTimeoutMs"] = "45000",
            ["Kafka:HeartbeatIntervalMs"] = "15000",
            ["Kafka:MaxPollIntervalMs"] = "600000"
        });

        var opts = new KafkaClientOptions();
        config.GetSection(KafkaClientOptions.SectionName).Bind(opts);

        Assert.AreEqual("broker1:9092,broker2:9092", opts.BootstrapServers);
        Assert.AreEqual(KafkaAuthMode.SaslSsl, opts.AuthMode);
        Assert.AreEqual("SCRAM-SHA-256", opts.Sasl.Mechanism);
        Assert.AreEqual("svc-dorc", opts.Sasl.Username);
        Assert.AreEqual("hunter2", opts.Sasl.Password);
        Assert.AreEqual("/etc/ssl/corp-root.pem", opts.SslCaLocation);
        Assert.AreEqual("https://registry.example:8081", opts.SchemaRegistry.Url);
        Assert.AreEqual("sr-user", opts.SchemaRegistry.BasicAuthUsername);
        Assert.AreEqual("sr-pass", opts.SchemaRegistry.BasicAuthPassword);
        Assert.AreEqual("dorc-monitor", opts.ConsumerGroupId);
        Assert.AreEqual(KafkaAutoOffsetReset.Latest, opts.AutoOffsetReset);
        Assert.IsTrue(opts.EnableAutoCommit);
        Assert.AreEqual(45_000, opts.SessionTimeoutMs);
        Assert.AreEqual(15_000, opts.HeartbeatIntervalMs);
        Assert.AreEqual(600_000, opts.MaxPollIntervalMs);
    }

    [TestMethod]
    public void Defaults_AreAppliedWhenOnlyBootstrapSupplied()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "localhost:9092"
        });

        var opts = new KafkaClientOptions();
        config.GetSection(KafkaClientOptions.SectionName).Bind(opts);

        Assert.AreEqual(KafkaAuthMode.Plaintext, opts.AuthMode);
        Assert.AreEqual(KafkaAutoOffsetReset.Earliest, opts.AutoOffsetReset);
        Assert.IsFalse(opts.EnableAutoCommit);
        Assert.AreEqual(30_000, opts.SessionTimeoutMs);
        Assert.AreEqual(10_000, opts.HeartbeatIntervalMs);
        Assert.AreEqual(300_000, opts.MaxPollIntervalMs);
    }

    [TestMethod]
    [DataRow("Kafka:BootstrapServers", "BootstrapServers")]
    [DataRow("Kafka:Sasl:Username", "Username")]
    [DataRow("Kafka:Sasl:Password", "Password")]
    public void Validate_OmittingMandatoryKey_FailsWithKeyNameInMessage(string omittedKey, string expectedKeyFragment)
    {
        var baseline = new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "broker1:9092",
            ["Kafka:AuthMode"] = "SaslSsl",
            ["Kafka:Sasl:Username"] = "svc-dorc",
            ["Kafka:Sasl:Password"] = "hunter2"
        };
        baseline.Remove(omittedKey);

        var opts = new KafkaClientOptions();
        BuildConfig(baseline).GetSection(KafkaClientOptions.SectionName).Bind(opts);

        var validator = new KafkaClientOptionsValidator();
        var result = validator.Validate(Options.DefaultName, opts);

        Assert.IsTrue(result.Failed, "Expected validation failure");
        StringAssert.Contains(string.Join("|", result.Failures!), expectedKeyFragment);
    }

    [TestMethod]
    public void Validate_InvalidSchemaRegistryUrl_FailsWithKeyNameInMessage()
    {
        var opts = new KafkaClientOptions
        {
            BootstrapServers = "broker1:9092",
            SchemaRegistry = { Url = "not-a-uri" }
        };

        var result = new KafkaClientOptionsValidator().Validate(Options.DefaultName, opts);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(string.Join("|", result.Failures!), nameof(KafkaSchemaRegistryOptions.Url));
    }

    [TestMethod]
    public void Validate_HeartbeatNotLessThanSessionTimeout_Fails()
    {
        var opts = new KafkaClientOptions
        {
            BootstrapServers = "broker1:9092",
            SessionTimeoutMs = 10_000,
            HeartbeatIntervalMs = 10_000,
            MaxPollIntervalMs = 300_000
        };

        var result = new KafkaClientOptionsValidator().Validate(Options.DefaultName, opts);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(string.Join("|", result.Failures!), nameof(KafkaClientOptions.HeartbeatIntervalMs));
    }

    [TestMethod]
    public void Validate_MaxPollNotGreaterThanSessionTimeout_Fails()
    {
        var opts = new KafkaClientOptions
        {
            BootstrapServers = "broker1:9092",
            SessionTimeoutMs = 30_000,
            HeartbeatIntervalMs = 10_000,
            MaxPollIntervalMs = 30_000
        };

        var result = new KafkaClientOptionsValidator().Validate(Options.DefaultName, opts);

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(string.Join("|", result.Failures!), nameof(KafkaClientOptions.MaxPollIntervalMs));
    }

    [TestMethod]
    public void OptionsBuilder_ValidateOnStart_ThrowsAtServiceResolve_WithKeyInMessage()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            // BootstrapServers deliberately missing
            ["Kafka:AuthMode"] = "Plaintext"
        });

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddOptions<KafkaClientOptions>()
            .Bind(config.GetSection(KafkaClientOptions.SectionName))
            .ValidateWithValidator();

        var sp = services.BuildServiceProvider();
        var ex = Assert.ThrowsExactly<OptionsValidationException>(
            () => _ = sp.GetRequiredService<IOptions<KafkaClientOptions>>().Value);

        StringAssert.Contains(ex.Message, nameof(KafkaClientOptions.BootstrapServers));
    }

    private static IConfigurationRoot BuildConfig(IDictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}

internal static class OptionsBuilderExtensions
{
    public static OptionsBuilder<KafkaClientOptions> ValidateWithValidator(this OptionsBuilder<KafkaClientOptions> builder)
    {
        builder.Services.AddSingleton<IValidateOptions<KafkaClientOptions>, KafkaClientOptionsValidator>();
        return builder;
    }
}

using Dorc.Core.Events;
using Dorc.Core.HighAvailability;
using Dorc.Core.Interfaces;
using Dorc.Monitor.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dorc.Monitor.Tests;

/// <summary>
/// Pins the fallback-path invariants of Monitor startup when Kafka is
/// disabled (or misconfigured). Program.cs uses top-level statements and
/// cannot be directly invoked from tests — these tests mirror the startup
/// decision logic to verify:
/// <list type="bullet">
///   <item><c>Kafka:Enabled=false</c> → NoOp lock service + SignalR publisher.</item>
///   <item>Empty <c>BootstrapServers</c> → same fallback (upgrade safety).</item>
///   <item>Empty <c>SchemaRegistry:Url</c> → same fallback.</item>
///   <item>No Kafka background (IHostedService) registered in fallback mode.</item>
/// </list>
///
/// <b>Known limitation:</b> <see cref="BuildFallbackRegistrations"/> is a
/// hand-copy of Program.cs's fallback-branch DI wiring rather than an
/// invocation of the production code itself; it must be kept in sync with
/// Program.cs manually, so the FallbackPath_* assertions verify the copy, not
/// the production path. The gate decision itself, however, IS production code:
/// <see cref="EvaluateKafkaEnabled"/> calls the shared
/// <c>KafkaStartupGate.IsKafkaEnabled</c> that both Program.cs files invoke.
/// </summary>
[TestClass]
public class KafkaStartupFallbackTests
{
    // The production gate shared by Monitor and API Program.cs.
    private static bool EvaluateKafkaEnabled(IConfiguration config)
        => Dorc.Kafka.Client.Configuration.KafkaStartupGate.IsKafkaEnabled(
            config, "test fallback mode", _ => { });

    private static IConfigurationRoot Config(params (string key, string? value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.key, p => p.value))
            .Build();

    // ── kafkaEnabled resolution ─────────────────────────────────────────────

    [TestMethod]
    public void KafkaEnabled_ExplicitFalse_ReturnsFalse()
    {
        var cfg = Config(("Kafka:Enabled", "false"));
        Assert.IsFalse(EvaluateKafkaEnabled(cfg));
    }

    [TestMethod]
    public void KafkaEnabled_TrueButBootstrapEmpty_ReturnsFalse()
    {
        var cfg = Config(
            ("Kafka:Enabled", "true"),
            ("Kafka:BootstrapServers", ""),
            ("Kafka:SchemaRegistry:Url", "http://localhost:8081"));
        Assert.IsFalse(EvaluateKafkaEnabled(cfg),
            "Empty BootstrapServers is the upgrade-safety guard — must trigger fallback.");
    }

    [TestMethod]
    public void KafkaEnabled_TrueButSchemaRegistryEmpty_ReturnsFalse()
    {
        var cfg = Config(
            ("Kafka:Enabled", "true"),
            ("Kafka:BootstrapServers", "localhost:9092"),
            ("Kafka:SchemaRegistry:Url", ""));
        Assert.IsFalse(EvaluateKafkaEnabled(cfg),
            "Empty SchemaRegistry:Url means Avro factory would throw at DI resolution — must fall back.");
    }

    [TestMethod]
    public void KafkaEnabled_FullyConfigured_ReturnsTrue()
    {
        var cfg = Config(
            ("Kafka:Enabled", "true"),
            ("Kafka:BootstrapServers", "localhost:9092"),
            ("Kafka:SchemaRegistry:Url", "http://localhost:8081"));
        Assert.IsTrue(EvaluateKafkaEnabled(cfg));
    }

    [TestMethod]
    public void KafkaEnabled_NotPresent_DefaultsToTrue()
    {
        // Kafka:Enabled defaults to true so that a fully configured broker
        // does not require an explicit opt-in setting.
        var cfg = Config(
            ("Kafka:BootstrapServers", "localhost:9092"),
            ("Kafka:SchemaRegistry:Url", "http://localhost:8081"));
        Assert.IsTrue(EvaluateKafkaEnabled(cfg), "Kafka:Enabled must default to true when the key is absent.");
    }

    [TestMethod]
    public void KafkaEnabled_SaslSslWithEmptyCredentials_ReturnsFalse()
    {
        // The WiX installers force Kafka:AuthMode=SaslSsl while the SASL
        // username/password MSI parameters default to empty — the gate must
        // treat that half-configured state as fallback, or ValidateOnStart
        // (KafkaClientOptionsValidator) crashes both hosts at startup instead.
        var cfg = Config(
            ("Kafka:Enabled", "true"),
            ("Kafka:BootstrapServers", "broker:9092"),
            ("Kafka:SchemaRegistry:Url", "https://registry:8081"),
            ("Kafka:AuthMode", "SaslSsl"),
            ("Kafka:Sasl:Username", ""),
            ("Kafka:Sasl:Password", ""));
        Assert.IsFalse(EvaluateKafkaEnabled(cfg),
            "SaslSsl with empty credentials must fall back cleanly, not crash at ValidateOnStart.");
    }

    [TestMethod]
    public void KafkaEnabled_SaslSslWithCredentials_ReturnsTrue()
    {
        var cfg = Config(
            ("Kafka:Enabled", "true"),
            ("Kafka:BootstrapServers", "broker:9092"),
            ("Kafka:SchemaRegistry:Url", "https://registry:8081"),
            ("Kafka:AuthMode", "SaslSsl"),
            ("Kafka:Sasl:Username", "dorc"),
            ("Kafka:Sasl:Password", "secret"),
            ("Kafka:Sasl:Mechanism", "SCRAM-SHA-256"));
        Assert.IsTrue(EvaluateKafkaEnabled(cfg));
    }

    [TestMethod]
    public void KafkaEnabled_SaslSslAsNumericEnum_EmptyCredentials_ReturnsFalse()
    {
        // The options binder accepts the numeric enum value; the gate must
        // parse AuthMode the same way or a numerically-configured install
        // sneaks past the gate and crashes at ValidateOnStart.
        var cfg = Config(
            ("Kafka:Enabled", "true"),
            ("Kafka:BootstrapServers", "broker:9092"),
            ("Kafka:SchemaRegistry:Url", "https://registry:8081"),
            ("Kafka:AuthMode", ((int)Dorc.Kafka.Client.Configuration.KafkaAuthMode.SaslSsl).ToString()),
            ("Kafka:Sasl:Username", ""),
            ("Kafka:Sasl:Password", ""));
        Assert.IsFalse(EvaluateKafkaEnabled(cfg),
            "Numeric AuthMode must be recognised — validator parity.");
    }

    [TestMethod]
    public void KafkaEnabled_SaslSslWithUnsupportedMechanism_ReturnsFalse()
    {
        // A typo'd deploy property (validator rejects unsupported mechanisms
        // at ValidateOnStart) must gate to fallback, not a startup crash.
        var cfg = Config(
            ("Kafka:Enabled", "true"),
            ("Kafka:BootstrapServers", "broker:9092"),
            ("Kafka:SchemaRegistry:Url", "https://registry:8081"),
            ("Kafka:AuthMode", "SaslSsl"),
            ("Kafka:Sasl:Username", "dorc"),
            ("Kafka:Sasl:Password", "secret"),
            ("Kafka:Sasl:Mechanism", "SCRAM-SHA-265"));
        Assert.IsFalse(EvaluateKafkaEnabled(cfg),
            "Unsupported SASL mechanism must fall back cleanly — validator parity.");
    }

    [TestMethod]
    public void KafkaEnabled_SaslSslWithBlankedMechanism_ReturnsFalse()
    {
        // An override channel blanking Kafka:Sasl:Mechanism fails the
        // validator; the gate must route that to fallback, not a crash.
        var cfg = Config(
            ("Kafka:Enabled", "true"),
            ("Kafka:BootstrapServers", "broker:9092"),
            ("Kafka:SchemaRegistry:Url", "https://registry:8081"),
            ("Kafka:AuthMode", "SaslSsl"),
            ("Kafka:Sasl:Username", "dorc"),
            ("Kafka:Sasl:Password", "secret"),
            ("Kafka:Sasl:Mechanism", ""));
        Assert.IsFalse(EvaluateKafkaEnabled(cfg));
    }

    [TestMethod]
    public void KafkaEnabled_PlaintextWithoutCredentials_ReturnsTrue()
    {
        // SASL completeness is only required when AuthMode is SaslSsl —
        // Plaintext (dev/compose) needs no credentials.
        var cfg = Config(
            ("Kafka:Enabled", "true"),
            ("Kafka:BootstrapServers", "localhost:9092"),
            ("Kafka:SchemaRegistry:Url", "http://localhost:8081"),
            ("Kafka:AuthMode", "Plaintext"));
        Assert.IsTrue(EvaluateKafkaEnabled(cfg));
    }

    // ── fallback service registrations ─────────────────────────────────────

    // Builds a ServiceCollection that mirrors Program.cs's fallback branch.
    // We inspect registrations rather than resolve instances so this test has
    // no dependency on SignalRDeploymentEventPublisher's full constructor
    // (which requires IMonitorConfiguration / a live hub URL).
    private static ServiceCollection BuildFallbackRegistrations()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Mirror Program.cs lines 146–177 (fallback branch).
        services.AddSingleton<SignalRDeploymentEventPublisher>();
        services.AddSingleton<IDeploymentEventsPublisher>(sp =>
            sp.GetRequiredService<SignalRDeploymentEventPublisher>());
        services.AddSingleton<IFallbackDeploymentEventPublisher>(sp =>
            sp.GetRequiredService<SignalRDeploymentEventPublisher>());

        services.AddSingleton<IDistributedLockService, NoOpDistributedLockService>();
        services.AddSingleton<IRequestPollSignal, RequestPollSignal>();
        return services;
    }

    [TestMethod]
    public void FallbackPath_RegistersNoOpDistributedLockService()
    {
        var services = BuildFallbackRegistrations();
        Assert.IsTrue(
            services.Any(sd =>
                sd.ServiceType == typeof(IDistributedLockService) &&
                sd.ImplementationType == typeof(NoOpDistributedLockService)),
            "Fallback path must register NoOpDistributedLockService, not the Kafka-backed one.");
    }

    [TestMethod]
    public void FallbackPath_RegistersSignalRPublisherForBothPublisherInterfaces()
    {
        var services = BuildFallbackRegistrations();
        Assert.IsTrue(
            services.Any(sd => sd.ServiceType == typeof(IDeploymentEventsPublisher)),
            "IDeploymentEventsPublisher must be registered in fallback mode.");
        Assert.IsTrue(
            services.Any(sd => sd.ServiceType == typeof(IFallbackDeploymentEventPublisher)),
            "IFallbackDeploymentEventPublisher must be registered in fallback mode.");
    }

    [TestMethod]
    public void FallbackPath_RegistersRequestPollSignal()
    {
        var services = BuildFallbackRegistrations();
        Assert.IsTrue(
            services.Any(sd =>
                sd.ServiceType == typeof(IRequestPollSignal) &&
                sd.ImplementationType == typeof(RequestPollSignal)),
            "IRequestPollSignal must be registered in fallback mode (DB poll still runs).");
    }

    [TestMethod]
    public void FallbackPath_NoKafkaHostedServicesRegistered()
    {
        // Kafka consumer background services (KafkaLockCoordinator,
        // DeploymentRequestsKafkaConsumer, etc.) must NOT be present in
        // fallback mode. They are only registered when kafkaEnabled=true.
        var services = BuildFallbackRegistrations();

        var kafkaHosted = services
            .Where(sd => sd.ServiceType == typeof(IHostedService))
            .Select(sd => sd.ImplementationType?.FullName
                         ?? sd.ImplementationFactory?.GetType().FullName
                         ?? "(factory)")
            .Where(name => name.Contains("Kafka", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.IsFalse(kafkaHosted.Count > 0,
            $"Kafka hosted service(s) registered in fallback mode: {string.Join(", ", kafkaHosted)}");
    }
}

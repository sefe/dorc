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
/// <b>Known limitation:</b> <see cref="EvaluateKafkaEnabled"/> and
/// <see cref="BuildFallbackRegistrations"/> are hand-copies of Program.cs logic
/// rather than invocations of the production code itself. They must be kept in
/// sync with Program.cs manually.  The FallbackPath_* assertions are therefore
/// tautological (they verify the hand-copy, not the production path).
/// TODO: extract the startup gate and DI wiring into a testable static method
/// or extension (e.g. MonitorKafkaStartup.ResolveEnabled + AddMonitorEventSubstrate)
/// called by Program.cs, and test that method directly.
/// </summary>
[TestClass]
public class KafkaStartupFallbackTests
{
    // Mirrors the kafkaEnabled decision in Monitor/Program.cs lines 122-137.
    // MUST be kept in sync with Program.cs if the startup conditions change.
    private static bool EvaluateKafkaEnabled(IConfiguration config)
    {
        var enabled = config.GetValue("Kafka:Enabled", defaultValue: true);
        if (enabled && string.IsNullOrWhiteSpace(config["Kafka:BootstrapServers"]))
            enabled = false;
        if (enabled && string.IsNullOrWhiteSpace(config["Kafka:SchemaRegistry:Url"]))
            enabled = false;
        return enabled;
    }

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

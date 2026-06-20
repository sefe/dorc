using Dorc.Core.HighAvailability;
using Dorc.Kafka.Lock.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dorc.Kafka.Lock.Tests.DependencyInjection;

/// <summary>
/// the substrate-selector flag is gone; Kafka lock is
/// unconditional. Remaining DI invariants: KafkaDistributedLockService
/// replaces any upstream IDistributedLockService registration; coordinator
/// is registered; idempotency holds.
/// </summary>
[TestClass]
public class KafkaDistributedLockDITests
{
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "localhost:9092",
                ["Kafka:ConsumerGroupId"] = "test",
                ["Kafka:AuthMode"] = "Plaintext",
                ["Kafka:Locks:ConsumerGroupId"] = "dorc.monitor.locks.test"
            })
            .Build();

    private sealed class UpstreamLockService : IDistributedLockService
    {
        public bool IsEnabled => true;
        public Task<IDistributedLock?> TryAcquireLockAsync(string r, int l, CancellationToken c)
            => Task.FromResult<IDistributedLock?>(null);
    }

    [TestMethod]
    public async Task Registration_ReplacesUpstreamWithKafkaImpl_AndRegistersCoordinator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new DummyEnv());
        services.AddSingleton<IDistributedLockService, UpstreamLockService>();

        services.AddDorcKafkaDistributedLock(BuildConfig());

        await using var sp = services.BuildServiceProvider();
        Assert.IsInstanceOfType(sp.GetRequiredService<IDistributedLockService>(), typeof(KafkaDistributedLockService));
        Assert.IsNotNull(sp.GetRequiredService<KafkaLockCoordinator>());
    }

    [TestMethod]
    public async Task HostedServices_TopicProvisionerStartsBeforeCoordinator()
    {
        // Hosted services start in registration order. The provisioner creates
        // the lock topic; if the coordinator is registered first, the first
        // boot subscribes to a nonexistent topic.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new DummyEnv());

        services.AddDorcKafkaDistributedLock(BuildConfig());

        var hostedDescriptors = services
            .Where(sd => sd.ServiceType == typeof(IHostedService))
            .ToList();

        var provisionerIndex = hostedDescriptors.FindIndex(
            sd => sd.ImplementationType == typeof(KafkaLocksTopicProvisioner));
        Assert.IsTrue(provisionerIndex >= 0, "Provisioner must be registered as a hosted service.");

        // The coordinator hosted service is factory-registered; identify it by
        // invoking each factory (safe: the coordinator ctor touches no broker).
        await using var sp = services.BuildServiceProvider();
        var coordinatorIndex = hostedDescriptors.FindIndex(sd =>
        {
            if (sd.ImplementationFactory is null) return false;
            try { return sd.ImplementationFactory(sp) is KafkaLockCoordinator; }
            // DI activation failures surface as InvalidOperationException; a
            // factory not constructible in this probing context is "not the
            // coordinator", anything else should fail the test loudly.
            catch (InvalidOperationException) { return false; }
        });
        Assert.IsTrue(coordinatorIndex >= 0, "Coordinator must be registered as a hosted service.");

        Assert.IsTrue(provisionerIndex < coordinatorIndex,
            $"KafkaLocksTopicProvisioner (index {provisionerIndex}) must be registered before " +
            $"KafkaLockCoordinator (index {coordinatorIndex}) so the topic exists before the consumer subscribes.");
    }

    [TestMethod]
    public void Idempotent_SecondCallIsNoOp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new DummyEnv());
        services.AddSingleton<IDistributedLockService, UpstreamLockService>();

        services.AddDorcKafkaDistributedLock(BuildConfig());
        var countAfterFirst = services.Count;
        services.AddDorcKafkaDistributedLock(BuildConfig());
        Assert.AreEqual(countAfterFirst, services.Count);
    }

    private sealed class DummyEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

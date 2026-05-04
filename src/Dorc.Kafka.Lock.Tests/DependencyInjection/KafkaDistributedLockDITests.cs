using Dorc.Core.HighAvailability;
using Dorc.Kafka.Lock.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dorc.Kafka.Lock.Tests.DependencyInjection;

/// <summary>
/// Post-SPEC-S-009 the substrate-selector flag is gone; Kafka lock is
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
                ["Kafka:AuthMode"] = "Plaintext"
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

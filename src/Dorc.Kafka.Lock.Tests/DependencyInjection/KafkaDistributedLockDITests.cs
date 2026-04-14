using Dorc.Core.HighAvailability;
using Dorc.Kafka.Events.Publisher;
using Dorc.Kafka.Lock.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dorc.Kafka.Lock.Tests.DependencyInjection;

/// <summary>
/// AT-8 — substrate flag switches behaviour. Also covers R-5 idempotency and
/// invalid-enum fail-fast.
/// </summary>
[TestClass]
public class KafkaDistributedLockDITests
{
    private static IConfiguration BuildConfig(string? distributedLockMode)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = "localhost:9092",
            ["Kafka:ConsumerGroupId"] = "test",
            ["Kafka:AuthMode"] = "Plaintext"
        };
        if (distributedLockMode is not null)
            dict[$"{KafkaSubstrateOptions.SectionName}:{nameof(KafkaSubstrateOptions.DistributedLock)}"] = distributedLockMode;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private sealed class UpstreamLockService : IDistributedLockService
    {
        public bool IsEnabled => true;
        public Task<IDistributedLock?> TryAcquireLockAsync(string r, int l, CancellationToken c)
            => Task.FromResult<IDistributedLock?>(null);
    }

    [TestMethod]
    public void Direct_RetainsUpstreamRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new DummyEnv());
        services.AddSingleton<IDistributedLockService, UpstreamLockService>();

        services.AddDorcKafkaDistributedLock(BuildConfig(distributedLockMode: null));

        using var sp = services.BuildServiceProvider();
        Assert.IsInstanceOfType(sp.GetRequiredService<IDistributedLockService>(), typeof(UpstreamLockService));
    }

    [TestMethod]
    public async Task Kafka_ReplacesUpstreamWithKafkaImplAndRegistersCoordinator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new DummyEnv());
        services.AddSingleton<IDistributedLockService, UpstreamLockService>();

        services.AddDorcKafkaDistributedLock(BuildConfig("Kafka"));

        await using var sp = services.BuildServiceProvider();
        Assert.IsInstanceOfType(sp.GetRequiredService<IDistributedLockService>(), typeof(KafkaDistributedLockService));
        Assert.IsNotNull(sp.GetRequiredService<KafkaLockCoordinator>());
    }

    [TestMethod]
    public void InvalidEnum_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            services.AddDorcKafkaDistributedLock(BuildConfig("Bogus")));
    }

    [TestMethod]
    public void Idempotent_SecondCallIsNoOp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new DummyEnv());
        services.AddSingleton<IDistributedLockService, UpstreamLockService>();

        services.AddDorcKafkaDistributedLock(BuildConfig("Kafka"));
        var countAfterFirst = services.Count;
        services.AddDorcKafkaDistributedLock(BuildConfig("Kafka"));
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

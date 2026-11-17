using Dorc.ApiModel;
using Dorc.Core;
using Dorc.Core.VariableResolution;
using Dorc.Monitor.Registry;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Dorc.Monitor.Tests
{
    /// <summary>
    /// DB-free R-6 gate (IS S-003/S-005): the Monitor-side registry must resolve the
    /// new environment-component sources the variable resolver will depend on at
    /// deploy time. The context factory is stubbed — no database is touched.
    /// </summary>
    [TestClass]
    public class PersistentSourcesRegistryResolutionTests
    {
        [TestMethod]
        public async Task PersistentSourcesRegistry_ResolvesNewComponentSources()
        {
            var collection = new ServiceCollection();
            PersistentSourcesRegistry.Register(collection);
            collection.AddLogging();
            collection.AddTransient(_ => Substitute.For<Microsoft.Extensions.Configuration.IConfiguration>());
            collection.AddTransient(_ => Substitute.For<IDeploymentContextFactory>());
            collection.AddTransient(_ => Substitute.For<ISecurityObjectFilter>());
            collection.AddTransient(_ => Substitute.For<IRolePrivilegesChecker>());
            collection.AddTransient(_ => Substitute.For<IPropertyEncryptor>());
            collection.AddTransient(_ => Substitute.For<IClaimsPrincipalReader>());
            collection.AddTransient(_ => Substitute.For<Dorc.Core.HighAvailability.IDistributedLockService>());

            using var provider = collection.BuildServiceProvider();

            Assert.IsNotNull(provider.GetRequiredService<IContainersPersistentSource>());
            Assert.IsNotNull(provider.GetRequiredService<ICloudResourcesPersistentSource>());
            Assert.IsNotNull(provider.GetRequiredService<IApiRegistrationsPersistentSource>());
            Assert.IsNotNull(provider.GetRequiredService<IEnvironmentTemplatesPersistentSource>());

            var alertPublisher = provider.GetRequiredService<ITeardownFailureAlertPublisher>();
            Assert.IsNotNull(alertPublisher);
            await alertPublisher.PublishTeardownFailureAlertAsync(
                new EnvironmentApiModel { EnvironmentId = 1, EnvironmentName = "eph-test" },
                "Test alert");

            var hostedServices = provider.GetServices<IHostedService>().ToList();
            Assert.IsTrue(hostedServices.Any(s => s is EphemeralEnvironmentReaperService));
        }

        [TestMethod]
        public void VariableScopeOptionsResolver_ResolvesWithComponentSources()
        {
            // Mirrors the Monitor's Program.cs registrations for the resolver's
            // dependency chain (R-6 gate: a missed registration must fail here, not at
            // deploy time). Only environment-supplied leaves are stubbed.
            var collection = new ServiceCollection();
            PersistentSourcesRegistry.Register(collection);
            collection.AddTransient(_ => Substitute.For<IDeploymentContextFactory>());
            collection.AddTransient(_ => Substitute.For<IRolePrivilegesChecker>());
            collection.AddTransient(_ => Substitute.For<IClaimsPrincipalReader>());
            collection.AddTransient(_ => Substitute.For<ISecurityObjectFilter>());
            collection.AddTransient(_ => Substitute.For<IPropertyEncryptor>());
            collection.AddLogging();
            collection.AddTransient<IVariableScopeOptionsResolver, VariableScopeOptionsResolver>();

            using var provider = collection.BuildServiceProvider();

            Assert.IsNotNull(provider.GetRequiredService<IVariableScopeOptionsResolver>());
        }
    }
}

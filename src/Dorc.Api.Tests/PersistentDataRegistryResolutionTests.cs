using Dorc.Api.Services;
using Dorc.Core.DatabaseAccess;
using Dorc.Core.Interfaces;
using Dorc.PersistentData;
using Dorc.PersistentData.Sources;
using Dorc.PersistentData.Sources.Interfaces;
using Lamar;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Dorc.Api.Tests
{
    /// <summary>
    /// DB-free R-6 gate (IS S-003): the API-side Lamar registry must resolve the new
    /// environment-component sources. DeploymentContextFactory only stores the
    /// connection string, so no database is touched.
    /// </summary>
    [TestClass]
    public class PersistentDataRegistryResolutionTests
    {
        [TestMethod]
        public void PersistentDataRegistry_ResolvesNewComponentSources()
        {
            using var container = new Container(new PersistentDataRegistry());

            Assert.IsNotNull(container.GetInstance<IContainersPersistentSource>());
            Assert.IsNotNull(container.GetInstance<ICloudResourcesPersistentSource>());
            Assert.IsNotNull(container.GetInstance<IApiRegistrationsPersistentSource>());
            Assert.IsNotNull(container.GetInstance<IContainerAuditPersistentSource>());
            Assert.IsNotNull(container.GetInstance<ICloudResourceAuditPersistentSource>());
            Assert.IsNotNull(container.GetInstance<IApiRegistrationAuditPersistentSource>());
            Assert.IsNotNull(container.GetInstance<IDatabaseAccessReader>());
        }

        [TestMethod]
        public void ApiRegistry_ResolvesDatabaseAccessRegistrations()
        {
            if (!OperatingSystem.IsWindows())
                Assert.Inconclusive("ApiRegistry targets Windows.");

            var dirFactory = Substitute.For<IDirectorySearcherFactory>();
            dirFactory.GetActiveDirectorySearcher().Returns(Substitute.For<IActiveDirectorySearcher>());

            using var container = new Container(registry =>
            {
                registry.IncludeRegistry<PersistentDataRegistry>();
                registry.IncludeRegistry<ApiRegistry>();
                registry.AddLogging();
                registry.For<Microsoft.Extensions.Configuration.IConfiguration>()
                    .Use(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
                registry.For<IDirectorySearcherFactory>().Use(dirFactory);
            });

            Assert.IsNotNull(container.GetInstance<DatabaseAccessStore>());
            Assert.IsNotNull(container.GetInstance<IDatabaseAccessProvider>());
            Assert.IsNotNull(container.GetInstance<DatabaseAccessProviders>());
            Assert.IsNotNull(container.GetInstance<DatabaseAccessDirectory>());
            Assert.IsNotNull(container.GetInstance<DatabaseAccessReconciler>());
        }
    }
}

using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Lamar;
using Microsoft.Extensions.Configuration;
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
        }

        [TestMethod]
        public void MetadataController_UsesInjectedConfigurationSettings_WithFallbacks()
        {
            var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<Dorc.Api.Controllers.MetadataController>>();
            var configSettings = Substitute.For<Dorc.Core.Configuration.IConfigurationSettings>();
            configSettings.GetEnvironment(false).Returns("DV 01");

            var controller = new Dorc.Api.Controllers.MetadataController(logger, configSettings);
            var result = controller.Get();
            var okResult = result as Microsoft.AspNetCore.Http.HttpResults.Ok<string>;

            Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>(result);
            StringAssert.StartsWith(okResult!.Value, "DV 01 - ");

            var fallbackController = new Dorc.Api.Controllers.MetadataController(logger);
            var fallbackResult = fallbackController.Get();
            Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<string>>(fallbackResult);
        }

        [TestMethod]
        public void OpenSearchDataRegistry_ResolvesFromHostConfigurationRoot()
        {
            var configRoot = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpenSearchSettings:ConnectionUri"] = "http://localhost:9200",
                    ["OpenSearchSettings:UserName"] = "admin",
                    ["OpenSearchSettings:Password"] = "password",
                    ["OpenSearchSettings:DeploymentResultIndex"] = "dorc-test-index"
                })
                .Build();

            using var container = new Container(x =>
            {
                x.For<Microsoft.Extensions.Configuration.IConfigurationRoot>().Use(configRoot);
                x.For(typeof(Microsoft.Extensions.Logging.ILogger<>)).Use(typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
                x.IncludeRegistry<Dorc.OpenSearchData.OpenSearchDataRegistry>();
            });

            Assert.IsNotNull(container.GetInstance<OpenSearch.Client.IOpenSearchClient>());
            Assert.IsNotNull(container.GetInstance<Dorc.OpenSearchData.Sources.Interfaces.IDeploymentLogService>());
        }
    }
}

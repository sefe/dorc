using Dorc.PersistentData;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Lamar;
using Microsoft.Extensions.Configuration;

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
            Assert.IsNotNull(container.GetInstance<IScriptScopeConfigValues>());
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void PersistentDataRegistry_UsesHostIConfigurationForScriptScopeConfigValues(bool scalarOverride)
        {
            var settings = scalarOverride
                ? new Dictionary<string, string?>
                {
                    [ScriptScopeConfigValues.WithheldKeysSetting] = "EnvOverrideOne, EnvOverrideTwo"
                }
                : new Dictionary<string, string?>
                {
                    [$"{ScriptScopeConfigValues.WithheldKeysSetting}:0"] = "EnvOverrideOne",
                    [$"{ScriptScopeConfigValues.WithheldKeysSetting}:1"] = "EnvOverrideTwo"
                };
            var hostConfiguration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            using var container = new Container(registry =>
            {
                registry.For<Microsoft.Extensions.Configuration.IConfiguration>().Use(hostConfiguration);
                registry.IncludeRegistry<PersistentDataRegistry>();
            });

            var resolved = container.GetInstance<IScriptScopeConfigValues>();

            Assert.IsTrue(resolved.IsWithheld("DORC_ProdDeployPassword"));
            Assert.IsTrue(resolved.IsWithheld("DORC_WebDeployPassword"));
            Assert.IsTrue(resolved.IsWithheld("DorcApiAccessPassword"));
            Assert.IsTrue(resolved.IsWithheld("EnvOverrideOne"));
            Assert.IsTrue(resolved.IsWithheld("EnvOverrideTwo"));
        }
    }
}

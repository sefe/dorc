using Dorc.PersistentData;
using Dorc.PersistentData.Sources.Interfaces;
using Lamar;

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
    }
}

using Dorc.Core.Configuration;
using Dorc.Core.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Core.Tests
{
    /// <summary>
    /// Four sites across two processes resolved the deployment credential independently, each
    /// with its own copy of the same four configuration-value key names and the same production
    /// boolean: the PowerShell dispatcher, the Terraform dispatcher, the daemon status probe
    /// running in the API process, and the password reset controller.
    ///
    /// Four copies of a security decision is four places for it to drift — and it is why the
    /// probe could be reached without authorization while the dispatchers could not. The key
    /// mapping now lives here, once.
    /// </summary>
    [TestClass]
    public class ConfigValueDeploymentCredentialSourceTests
    {
        private IConfigValuesPersistentSource _configValues = null!;
        private ConfigValueDeploymentCredentialSource _source = null!;

        [TestInitialize]
        public void Setup()
        {
            _configValues = Substitute.For<IConfigValuesPersistentSource>();
            _configValues.GetConfigValue("DORC_ProdDeployUsername").Returns("svc-prod");
            _configValues.GetConfigValue("DORC_ProdDeployPassword").Returns("prod-secret");
            _configValues.GetConfigValue("DORC_NonProdDeployUsername").Returns("svc-nonprod");
            _configValues.GetConfigValue("DORC_NonProdDeployPassword").Returns("nonprod-secret");

            _source = new ConfigValueDeploymentCredentialSource(
                _configValues,
                Substitute.For<ILogger<ConfigValueDeploymentCredentialSource>>());
        }

        [TestMethod]
        public void ResolvesTheProductionPairForTheProductionTier()
        {
            var credential = _source.Resolve(DeploymentTier.Production);

            Assert.AreEqual("svc-prod", credential!.UserName);
            Assert.IsTrue(credential.IsComplete);
        }

        [TestMethod]
        public void ResolvesTheNonProductionPairForTheNonProductionTier()
        {
            var credential = _source.Resolve(DeploymentTier.NonProduction);

            Assert.AreEqual("svc-nonprod", credential!.UserName);
            Assert.IsTrue(credential.IsComplete);
        }

        /// <summary>
        /// Null, not a partial credential. A caller that proceeds without one authenticates as
        /// whatever the host happens to be running as.
        /// </summary>
        [TestMethod]
        public void RefusesRatherThanReturningAnIncompletePair()
        {
            _configValues.GetConfigValue("DORC_ProdDeployPassword").Returns(string.Empty);

            Assert.IsNull(_source.Resolve(DeploymentTier.Production));
        }

        [TestMethod]
        public void SaysWhereItReadsFrom()
        {
            StringAssert.Contains(_source.Description, "configuration values");
        }
    }

    /// <summary>
    /// The vault-backed source is the migration target: selecting it is what removes deployment
    /// credentials from DOrc's database. It lives in the shared assembly rather than the API's
    /// so the Monitor can use it at all — before the relocation it could not have, however it
    /// was configured.
    /// </summary>
    [TestClass]
    public class VaultDeploymentCredentialSourceTests
    {
        private IConfigurationSecretsReader _secrets = null!;
        private IConfigurationSettings _configuration = null!;
        private VaultDeploymentCredentialSource _source = null!;

        [TestInitialize]
        public void Setup()
        {
            _secrets = Substitute.For<IConfigurationSecretsReader>();
            _configuration = Substitute.For<IConfigurationSettings>();

            _configuration.GetDeploymentCredentialItemId(DeploymentTier.Production, false).Returns("prod-user-item");
            _configuration.GetDeploymentCredentialItemId(DeploymentTier.Production, true).Returns("prod-pass-item");

            _secrets.GetSecret("prod-user-item", Arg.Any<string>()).Returns("svc-prod");
            _secrets.GetSecureSecret("prod-pass-item", Arg.Any<string>())
                .Returns(DeploymentCredential.ToSecureString("prod-secret"));

            _source = new VaultDeploymentCredentialSource(
                _secrets, _configuration,
                Substitute.For<ILogger<VaultDeploymentCredentialSource>>());
        }

        [TestMethod]
        public void ResolvesFromTheConfiguredVaultItems()
        {
            var credential = _source.Resolve(DeploymentTier.Production);

            Assert.AreEqual("svc-prod", credential!.UserName);
            Assert.IsTrue(credential.IsComplete);
        }

        /// <summary>
        /// Selected as the source but not told where to look. Refusing is the only safe answer:
        /// falling back to configuration values would silently undo the decision to stop keeping
        /// deployment credentials in DOrc's database.
        /// </summary>
        [TestMethod]
        public void RefusesRatherThanFallingBackWhenItemsAreNotConfigured()
        {
            Assert.IsNull(_source.Resolve(DeploymentTier.NonProduction));

            _secrets.DidNotReceive().GetSecret(Arg.Any<string>(), Arg.Any<string>());
            _secrets.DidNotReceive().GetSecureSecret(Arg.Any<string>(), Arg.Any<string>());
        }

        [TestMethod]
        public void RefusesWhenTheVaultReturnsNothing()
        {
            _secrets.GetSecureSecret("prod-pass-item", Arg.Any<string>())
                .Returns(DeploymentCredential.ToSecureString(string.Empty));

            Assert.IsNull(_source.Resolve(DeploymentTier.Production));
        }

        [TestMethod]
        public void SaysWhereItReadsFrom()
        {
            StringAssert.Contains(_source.Description, "vault");
        }

        /// <summary>
        /// Both implementations satisfy the same contract for the same reference, which is what
        /// makes switching between them a configuration change rather than a code change.
        /// </summary>
        [TestMethod]
        public void ResolvesTheSameCredentialAsTheConfigurationValueSourceWouldFor()
        {
            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            configValues.GetConfigValue("DORC_ProdDeployUsername").Returns("svc-prod");
            configValues.GetConfigValue("DORC_ProdDeployPassword").Returns("prod-secret");

            var fromConfig = new ConfigValueDeploymentCredentialSource(
                configValues,
                Substitute.For<ILogger<ConfigValueDeploymentCredentialSource>>())
                .Resolve(DeploymentTier.Production);

            var fromVault = _source.Resolve(DeploymentTier.Production);

            Assert.AreEqual(fromConfig!.UserName, fromVault!.UserName);
            Assert.IsTrue(fromConfig.IsComplete);
            Assert.IsTrue(fromVault.IsComplete);
        }
    }
}

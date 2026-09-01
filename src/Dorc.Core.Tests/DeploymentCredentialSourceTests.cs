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
            Assert.AreEqual("prod-secret", credential.Password);
        }

        [TestMethod]
        public void ResolvesTheNonProductionPairForTheNonProductionTier()
        {
            var credential = _source.Resolve(DeploymentTier.NonProduction);

            Assert.AreEqual("svc-nonprod", credential!.UserName);
            Assert.AreEqual("nonprod-secret", credential.Password);
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
            _secrets.GetSecret("prod-pass-item", Arg.Any<string>()).Returns("prod-secret");

            _source = new VaultDeploymentCredentialSource(
                _secrets, _configuration,
                Substitute.For<ILogger<VaultDeploymentCredentialSource>>());
        }

        [TestMethod]
        public void ResolvesFromTheConfiguredVaultItems()
        {
            var credential = _source.Resolve(DeploymentTier.Production);

            Assert.AreEqual("svc-prod", credential!.UserName);
            Assert.AreEqual("prod-secret", credential.Password);
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
        }

        [TestMethod]
        public void RefusesWhenTheVaultReturnsNothing()
        {
            _secrets.GetSecret("prod-pass-item", Arg.Any<string>()).Returns(string.Empty);

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
            Assert.AreEqual(fromConfig.Password, fromVault.Password);
        }
    }
    /// <summary>
    /// Execution identity was a boolean — production or not — which is why one compromised
    /// deployment reaches the whole estate. An environment may now name its own identity.
    ///
    /// The interesting property is a negative one: an environment that names nothing must
    /// resolve exactly as it did before. That is what lets migration proceed environment by
    /// environment instead of as a flag day, and it is why the keying and the fallback
    /// accounting live in one shared place rather than once per implementation.
    /// </summary>
    [TestClass]
    public class EnvironmentKeyedCredentialResolutionTests
    {
        private IConfigValuesPersistentSource _configValues = null!;
        private ConfigValueDeploymentCredentialSource _source = null!;

        [TestInitialize]
        public void Setup()
        {
            _configValues = Substitute.For<IConfigValuesPersistentSource>();
            _configValues.GetConfigValue("DORC_NonProdDeployUsername").Returns("svc-shared");
            _configValues.GetConfigValue("DORC_NonProdDeployPassword").Returns("shared-secret");
            _configValues.GetConfigValue("DORC_Deploy_trading-tier_Username").Returns("svc-trading");
            _configValues.GetConfigValue("DORC_Deploy_trading-tier_Password").Returns("trading-secret");

            _source = new ConfigValueDeploymentCredentialSource(
                _configValues,
                Substitute.For<ILogger<ConfigValueDeploymentCredentialSource>>());
        }

        [TestMethod]
        public void AnEnvironmentNamingAnIdentityDeploysUnderIt()
        {
            var credential = _source.Resolve(DeploymentTier.NonProduction, "trading-tier");

            Assert.AreEqual("svc-trading", credential!.UserName);
        }

        /// <summary>
        /// The property that makes this safe to ship.
        /// </summary>
        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void AnEnvironmentNamingNothingResolvesExactlyAsBefore(string? identityReference)
        {
            var credential = _source.Resolve(DeploymentTier.NonProduction, identityReference);

            Assert.AreEqual("svc-shared", credential!.UserName);
        }

        /// <summary>
        /// Deliberately NOT a fallback. An environment bound to an identity was bound on
        /// purpose; quietly running it under the shared account would undo the binding without
        /// anyone noticing, and the whole point of binding is that one compromised deployment
        /// does not reach the estate.
        /// </summary>
        [TestMethod]
        public void AnUnresolvableIdentityRefusesRatherThanFallingBackToTheSharedAccount()
        {
            Assert.IsNull(_source.Resolve(DeploymentTier.NonProduction, "no-such-identity"));
        }

        [TestMethod]
        public void AnInvalidPersistedIdentityReferenceIsRefusedBeforeCredentialLookup()
        {
            Assert.IsNull(_source.Resolve(DeploymentTier.NonProduction, "../secret"));

            _configValues.DidNotReceive().GetConfigValue("DORC_Deploy_../secret_Username");
            _configValues.DidNotReceive().GetConfigValue("DORC_Deploy_../secret_Password");
        }

        /// <summary>
        /// "We have started binding identities" is not the same claim as "the estate is bound".
        /// Reported from the count rather than from a configuration flag, so it cannot be wrong.
        /// </summary>
        [TestMethod]
        public void CountsBoundResolutionsSeparatelyFromFallbacks()
        {
            _source.Resolve(DeploymentTier.NonProduction, "trading-tier");
            _source.Resolve(DeploymentTier.NonProduction, null);
            _source.Resolve(DeploymentTier.NonProduction, "   ");

            var (bound, fallback) = _source.ResolutionCounts;

            Assert.AreEqual(1, bound);
            Assert.AreEqual(2, fallback);
        }

        /// <summary>
        /// A refused identity is neither bound nor a fallback — counting it as either would
        /// misreport migration progress in one direction or the other.
        /// </summary>
        [TestMethod]
        public void ARefusedIdentityCountsAsNeither()
        {
            _source.Resolve(DeploymentTier.NonProduction, "no-such-identity");

            Assert.AreEqual((0, 0), _source.ResolutionCounts);
        }
    }
}

using Dorc.Api.Tests.Mocks;
using Dorc.ApiModel;
using Dorc.PersistentData;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources;
using NSubstitute;

namespace Dorc.Api.Tests.Sources
{
    /// <summary>
    /// The classification's default is asymmetric, and the asymmetry is the point.
    ///
    /// Existing values default to visible — in the column default — so that adding the column
    /// changes no deployment's behaviour on upgrade. New secure values are created hidden. That
    /// is what makes the classification safe to ship without an estate-wide inventory of which
    /// scripts consume what: nothing breaks today, and administrators tighten per key at their
    /// own pace.
    ///
    /// A symmetric default would force the choice between breaking deployments on day one and
    /// shipping a control that protects nothing new.
    /// </summary>
    [TestClass]
    public class ConfigValueScriptVisibilityTests
    {
        private List<ConfigValue> _stored = null!;
        private IDeploymentContext _context = null!;
        private ConfigValuesPersistentSource _source = null!;

        [TestInitialize]
        public void Setup()
        {
            _stored = new List<ConfigValue>();

            // Built into a local first: GetQueryableMockDbSet configures another substitute
            // internally, and NSubstitute cannot have that happen inside a Returns() argument.
            var configValues = DbContextMock.GetQueryableMockDbSet(_stored);

            _context = Substitute.For<IDeploymentContext>();
            _context.ConfigValues.Returns(configValues);

            var contextFactory = Substitute.For<IDeploymentContextFactory>();
            contextFactory.GetContext().Returns(_context);

            var encryptor = Substitute.For<IPropertyEncryptor>();
            encryptor.EncryptValue(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());

            _source = new ConfigValuesPersistentSource(contextFactory, encryptor);
        }

        [TestMethod]
        public void ANewSecureValueIsCreatedHiddenFromScripts()
        {
            _source.Add(new ConfigValueApiModel
            {
                Key = "SomeNewSecret",
                Value = "s3cret",
                Secure = true,
                VisibleToScripts = false
            });

            Assert.IsFalse(_stored.Single().VisibleToScripts);
        }

        /// <summary>
        /// A non-secure value carries no credential, and scripts legitimately consume plenty of
        /// them. Creating those hidden would break working deployments for no gain.
        /// </summary>
        [TestMethod]
        public void ANewNonSecureValueIsVisibleToScripts()
        {
            _source.Add(new ConfigValueApiModel
            {
                Key = "DeploymentLogDir",
                Value = @"\\host\logs",
                Secure = false,
                VisibleToScripts = false
            });

            Assert.IsTrue(_stored.Single().VisibleToScripts,
                "Hiding a non-secure value is not something the creator can ask for by accident.");
        }

        [TestMethod]
        public void ANewSecureValueMayBeCreatedVisibleWhenItsCreatorSaysSo()
        {
            _source.Add(new ConfigValueApiModel
            {
                Key = "DeploymentServiceAccountPassword",
                Value = "s3cret",
                Secure = true,
                VisibleToScripts = true
            });

            Assert.IsTrue(_stored.Single().VisibleToScripts,
                "Some secure values are deployment inputs by design and must stay reachable.");
        }

        /// <summary>
        /// Toggling visibility on a value whose text is unchanged must not be mistaken for a
        /// no-op — the update path handles one property change per request, so the branch order
        /// matters.
        /// </summary>
        [TestMethod]
        public void VisibilityCanBeChangedWithoutChangingTheValue()
        {
            _stored.Add(new ConfigValue
            {
                Id = 7,
                Key = "SomeSecret",
                Value = "enc:s3cret",
                Secure = true,
                VisibleToScripts = true
            });

            var updated = _source.UpdateConfigValue(new ConfigValueApiModel
            {
                Id = 7,
                Key = "SomeSecret",
                Secure = true,
                VisibleToScripts = false
            });

            Assert.IsFalse(_stored.Single().VisibleToScripts);
            Assert.IsFalse(updated.VisibleToScripts, "The classification must survive the round trip.");
        }

        [TestMethod]
        public void TheClassificationIsReportedBackToCallers()
        {
            var hidden = new ConfigValue
            {
                Id = 1, Key = "Hidden", Value = "enc:x", Secure = true, VisibleToScripts = false
            };
            _stored.Add(hidden);

            // GetAllConfigValues reads through the context method rather than the DbSet.
            _context.GetAllConfigValues().Returns(_stored);

            Assert.IsFalse(_source.GetAllConfigValues(false).Single().VisibleToScripts);
        }
    }
}

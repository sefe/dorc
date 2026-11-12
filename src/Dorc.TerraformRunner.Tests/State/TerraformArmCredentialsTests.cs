using Dorc.ApiModel.MonitorRunnerApi;
using Dorc.Terraform.Catalog;

namespace Dorc.TerraformRunner.Tests.State
{
    [TestClass]
    public class TerraformArmCredentialsTests
    {
        private const string ClientId = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
        private const string TenantId = "99999999-8888-4777-8666-555555555555";
        private const string Secret = "s3cret-value";

        private static IDictionary<string, VariableValue> Properties(
            string? clientId = null, string? secret = null, string? tenantId = null)
        {
            var props = new Dictionary<string, VariableValue>();
            if (clientId is not null)
                props[TerraformArmCredentials.ClientIdPropertyName] = new VariableValue { Value = clientId, Type = typeof(string) };
            if (secret is not null)
                props[TerraformArmCredentials.ClientSecretPropertyName] = new VariableValue { Value = secret, Type = typeof(string) };
            if (tenantId is not null)
                props[TerraformArmCredentials.TenantIdPropertyName] = new VariableValue { Value = tenantId, Type = typeof(string) };
            return props;
        }

        [TestMethod]
        public void Resolve_ReturnsNull_WhenNoCredentialPropertiesSet()
        {
            Assert.IsNull(TerraformArmCredentials.Resolve(null));
            Assert.IsNull(TerraformArmCredentials.Resolve(new Dictionary<string, VariableValue>()));
            // Whitespace-only values count as unset.
            Assert.IsNull(TerraformArmCredentials.Resolve(Properties(clientId: " ", secret: "", tenantId: "  ")));
        }

        [TestMethod]
        public void Resolve_ReturnsCredentials_WhenAllThreeSet()
        {
            var credentials = TerraformArmCredentials.Resolve(Properties(ClientId, Secret, TenantId));

            Assert.IsNotNull(credentials);
            Assert.AreEqual(ClientId, credentials.ClientId);
            Assert.AreEqual(Secret, credentials.ClientSecret);
            Assert.AreEqual(TenantId, credentials.TenantId);
        }

        [TestMethod]
        public void Resolve_TrimsValues()
        {
            var credentials = TerraformArmCredentials.Resolve(
                Properties($" {ClientId} ", $" {Secret} ", $" {TenantId} "));

            Assert.AreEqual(ClientId, credentials!.ClientId);
            Assert.AreEqual(Secret, credentials.ClientSecret);
            Assert.AreEqual(TenantId, credentials.TenantId);
        }

        [TestMethod]
        public void Resolve_Throws_OnPartialConfiguration_NamingMissingProperties()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => TerraformArmCredentials.Resolve(Properties(clientId: ClientId)));

            StringAssert.Contains(ex.Message, TerraformArmCredentials.ClientSecretPropertyName);
            StringAssert.Contains(ex.Message, TerraformArmCredentials.TenantIdPropertyName);
            Assert.IsFalse(ex.Message.Contains(Secret));
        }

        [TestMethod]
        public void Resolve_Throws_WhenClientIdIsNotAGuid()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => TerraformArmCredentials.Resolve(Properties("not-a-guid", Secret, TenantId)));

            StringAssert.Contains(ex.Message, TerraformArmCredentials.ClientIdPropertyName);
        }

        [TestMethod]
        public void Resolve_Throws_WhenTenantIdIsNotAGuid()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => TerraformArmCredentials.Resolve(Properties(ClientId, Secret, "nope")));

            StringAssert.Contains(ex.Message, TerraformArmCredentials.TenantIdPropertyName);
        }

        [TestMethod]
        public void ToArmEnvironment_MapsCredentialsToArmVariables()
        {
            var credentials = TerraformArmCredentials.Resolve(Properties(ClientId, Secret, TenantId))!;

            var env = credentials.ToArmEnvironment();

            Assert.AreEqual(3, env.Count);
            Assert.AreEqual(ClientId, env["ARM_CLIENT_ID"]);
            Assert.AreEqual(Secret, env["ARM_CLIENT_SECRET"]);
            Assert.AreEqual(TenantId, env["ARM_TENANT_ID"]);
        }

        [TestMethod]
        public void ToArmEnvironment_IncludesSubscription_WhenSupplied()
        {
            var credentials = TerraformArmCredentials.Resolve(Properties(ClientId, Secret, TenantId))!;

            var env = credentials.ToArmEnvironment(" 11111111-2222-4333-8444-555555555555 ");

            Assert.AreEqual("11111111-2222-4333-8444-555555555555", env["ARM_SUBSCRIPTION_ID"]);
        }

        [TestMethod]
        public void ResolveAivenApiToken_ReturnsNull_WhenUnset()
        {
            Assert.IsNull(TerraformArmCredentials.ResolveAivenApiToken(null));
            Assert.IsNull(TerraformArmCredentials.ResolveAivenApiToken(new Dictionary<string, VariableValue>()));
            Assert.IsNull(TerraformArmCredentials.ResolveAivenApiToken(new Dictionary<string, VariableValue>
            {
                [TerraformArmCredentials.AivenApiTokenPropertyName] = new VariableValue { Value = "  ", Type = typeof(string) },
            }));
        }

        [TestMethod]
        public void ResolveAivenApiToken_ReturnsTrimmedToken_IndependentOfArmCredentials()
        {
            var props = new Dictionary<string, VariableValue>
            {
                [TerraformArmCredentials.AivenApiTokenPropertyName] = new VariableValue { Value = " aiven-t0ken== ", Type = typeof(string) },
            };

            Assert.AreEqual("aiven-t0ken==", TerraformArmCredentials.ResolveAivenApiToken(props));
            // Token alone must not count as partial ARM credential configuration.
            Assert.IsNull(TerraformArmCredentials.Resolve(props));
        }
    }
}

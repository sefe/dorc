using System.Data;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Dorc.Core.Configuration;
using Dorc.Core.Secrets;
using Dorc.Core.Secrets.OnePassword;
using Dorc.Core.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
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
            using var credential = _source.Resolve(DeploymentTier.Production);

            Assert.AreEqual("svc-prod", credential!.UserName);
            Assert.IsTrue(credential.IsComplete);
        }

        [TestMethod]
        public void ResolvesTheNonProductionPairForTheNonProductionTier()
        {
            using var credential = _source.Resolve(DeploymentTier.NonProduction);

            Assert.AreEqual("svc-nonprod", credential!.UserName);
            Assert.IsTrue(credential.IsComplete);
        }

        /// <summary>
        /// Null, not a partial credential. A caller that proceeds without one authenticates as
        /// whatever the host happens to be running as.
        /// </summary>
        [TestMethod]
        public void RefusesRatherThanReturningAnIncompletePairAndDoesNotIncrementFallbackCount()
        {
            _configValues.GetConfigValue("DORC_ProdDeployPassword").Returns(string.Empty);

            Assert.IsNull(_source.Resolve(DeploymentTier.Production));
            Assert.AreEqual((0, 0), _source.ResolutionCounts);
        }

        [TestMethod]
        public void SaysWhereItReadsFrom()
        {
            StringAssert.Contains(_source.Description, "configuration values");
        }

        [TestMethod]
        public void DisposingDeploymentCredentialDisposesUnderlyingSecureString()
        {
            var credential = DeploymentCredential.FromPlainText("svc-prod", "prod-secret");
            var password = credential.Password;
            Assert.AreEqual(11, password.Length);

            credential.Dispose();

            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = password.Length);
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

            _secrets.GetPrincipalIdentifierByItemId("prod-user-item", Arg.Any<string>()).Returns("svc-prod");
            _secrets.GetSecureSecret("prod-pass-item", Arg.Any<string>())
                .Returns(_ => DeploymentCredential.ToSecureString("prod-secret"));

            _source = new VaultDeploymentCredentialSource(
                _secrets, _configuration,
                Substitute.For<ILogger<VaultDeploymentCredentialSource>>());
        }

        [TestMethod]
        public void ResolvesFromTheConfiguredVaultItems()
        {
            using var credential = _source.Resolve(DeploymentTier.Production);

            Assert.AreEqual("svc-prod", credential!.UserName);
            Assert.IsTrue(credential.IsComplete);
            _secrets.DidNotReceive().GetSecretByItemId(Arg.Any<string>(), Arg.Any<string>());
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

            _secrets.DidNotReceive().GetPrincipalIdentifierByItemId(Arg.Any<string>(), Arg.Any<string>());
            _secrets.DidNotReceive().GetSecretByItemId(Arg.Any<string>(), Arg.Any<string>());
            _secrets.DidNotReceive().GetSecureSecret(Arg.Any<string>(), Arg.Any<string>());
        }

        [TestMethod]
        public void RefusesWhenTheVaultReturnsNothing()
        {
            var emptySecret = DeploymentCredential.ToSecureString(string.Empty);
            _secrets.GetSecureSecret("prod-pass-item", Arg.Any<string>())
                .Returns(emptySecret);

            Assert.IsNull(_source.Resolve(DeploymentTier.Production));
            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = emptySecret.Length);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("Not Found")]
        [DataRow("not found")]
        public void RefusesWhenPrincipalIdentifierIsMissingWhitespaceOrSentinelAndDisposesSecret(string invalidUserName)
        {
            var candidateSecret = DeploymentCredential.ToSecureString("prod-secret");
            _secrets.GetPrincipalIdentifierByItemId("prod-user-item", Arg.Any<string>()).Returns(invalidUserName);
            _secrets.GetSecureSecret("prod-pass-item", Arg.Any<string>()).Returns(candidateSecret);

            Assert.IsNull(_source.Resolve(DeploymentTier.Production));
            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = candidateSecret.Length);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("Not Found")]
        [DataRow("not found")]
        public void RefusesWhenPasswordIsMissingWhitespaceOrSentinel(string invalidPassword)
        {
            var invalidSecret = DeploymentCredential.ToSecureString(invalidPassword);
            _secrets.GetSecureSecret("prod-pass-item", Arg.Any<string>())
                .Returns(invalidSecret);

            Assert.IsNull(_source.Resolve(DeploymentTier.Production));
            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = invalidSecret.Length);
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

            using var fromConfig = new ConfigValueDeploymentCredentialSource(
                configValues,
                Substitute.For<ILogger<ConfigValueDeploymentCredentialSource>>())
                .Resolve(DeploymentTier.Production);

            using var fromVault = _source.Resolve(DeploymentTier.Production);

            Assert.AreEqual(fromConfig!.UserName, fromVault!.UserName);
            Assert.IsTrue(fromConfig.IsComplete);
            Assert.IsTrue(fromVault.IsComplete);
        }
    }

    [TestClass]
    public class OnePasswordVaultCredentialResolutionTests
    {
        private const string LoginUuid = "loginitem00000000000000001";
        private const string SharedApiCredUuid = "sharedapicred0000000000001";
        private const string UserApiCredUuid = "userapicred000000000000001";
        private const string PassApiCredUuid = "passapicred000000000000001";
        private const string TradingUserUuid = "tradinguser000000000000001";
        private const string TradingPassUuid = "tradingpass000000000000001";
        private const string DuplicatePassUuid = "tradingpass000000000000002";
        private const string EscapedTitleUuid = "escapeditem000000000000001";

        [TestMethod]
        public void LoginItem_ReadsUsernameIntoUserNameAndPasswordIntoPassword_NeverLeakingPasswordIntoUserName()
        {
            const string loginJson = $$"""
                {
                  "id": "{{LoginUuid}}",
                  "category": "LOGIN",
                  "title": "DOrc Prod Deploy",
                  "fields": [
                    { "id": "username", "type": "STRING", "label": "username", "value": "svc_dorc_prod" },
                    { "id": "password", "type": "CONCEALED", "label": "password", "value": "UltraSecret!Pass#99" }
                  ]
                }
                """;

            using var harness = CreateVaultHarness(
                new Dictionary<string, string> { [LoginUuid] = loginJson },
                userNameItemId: LoginUuid,
                passwordItemId: LoginUuid);

            using var credential = harness.Source.Resolve(DeploymentTier.Production);

            Assert.IsNotNull(credential);
            Assert.AreEqual("svc_dorc_prod", credential.UserName);
            Assert.AreNotEqual("UltraSecret!Pass#99", credential.UserName);
            Assert.AreEqual("UltraSecret!Pass#99", ReadSecureString(credential.Password));
            CollectionAssert.AreEqual(
                new[]
                {
                    $"/v1/vaults/vault-1/items/{LoginUuid}",
                    $"/v1/vaults/vault-1/items/{LoginUuid}"
                },
                harness.Handler.RequestedPathsAndQueries.ToArray());
        }

        [TestMethod]
        [DataRow($$"""{"id":"{{LoginUuid}}","category":"LOGIN","fields":[{"id":"password","value":"SecretOnly"}]}""")]
        [DataRow($$"""{"id":"{{LoginUuid}}","category":"LOGIN","fields":[{"id":"username","value":"Not Found"},{"id":"password","value":"SecretOnly"}]}""")]
        [DataRow($$"""{"id":"{{LoginUuid}}","category":"LOGIN","fields":[{"id":"username","value":"svc_dorc_prod"}]}""")]
        [DataRow($$"""{"id":"{{LoginUuid}}","category":"LOGIN","fields":[{"id":"username","value":"svc_dorc_prod"},{"id":"password","value":"Not Found"}]}""")]
        public void LoginItem_FailsClosedWhenUsernameOrPasswordFieldIsMissingOrSentinel(string itemJson)
        {
            using var harness = CreateVaultHarness(
                new Dictionary<string, string> { [LoginUuid] = itemJson },
                userNameItemId: LoginUuid,
                passwordItemId: LoginUuid);

            Assert.IsNull(harness.Source.Resolve(DeploymentTier.Production));
        }

        [TestMethod]
        public void SharedApiCredentialItemWithoutUsernameField_RefusesRatherThanPuttingCredentialInUserName()
        {
            const string apiCredJson = $$"""
                {
                  "id": "{{SharedApiCredUuid}}",
                  "category": "API_CREDENTIAL",
                  "fields": [
                    { "id": "username", "value": "" },
                    { "id": "credential", "value": "SharedApiSecretValue" }
                  ]
                }
                """;

            using var harness = CreateVaultHarness(
                new Dictionary<string, string> { [SharedApiCredUuid] = apiCredJson },
                userNameItemId: SharedApiCredUuid,
                passwordItemId: SharedApiCredUuid);

            Assert.IsNull(harness.Source.Resolve(DeploymentTier.Production));
        }

        [TestMethod]
        public void SeparateApiCredentialItems_ResolvesDedicatedUsernameAndPasswordItems()
        {
            const string userItemJson = $$"""
                {
                  "id": "{{UserApiCredUuid}}",
                  "category": "API_CREDENTIAL",
                  "fields": [
                    { "id": "credential", "value": "svc_nonprod" },
                    { "id": "type", "value": "other" }
                  ]
                }
                """;
            const string passItemJson = $$"""
                {
                  "id": "{{PassApiCredUuid}}",
                  "category": "API_CREDENTIAL",
                  "fields": [
                    { "id": "credential", "value": "NonProdSecret!42" }
                  ]
                }
                """;

            using var harness = CreateVaultHarness(
                new Dictionary<string, string>
                {
                    [UserApiCredUuid] = userItemJson,
                    [PassApiCredUuid] = passItemJson
                },
                userNameItemId: UserApiCredUuid,
                passwordItemId: PassApiCredUuid);

            using var credential = harness.Source.Resolve(DeploymentTier.Production);

            Assert.IsNotNull(credential);
            Assert.AreEqual("svc_nonprod", credential.UserName);
            Assert.AreEqual("NonProdSecret!42", ReadSecureString(credential.Password));
        }

        [TestMethod]
        public void ParseSecureSecretFromUtf8Json_HandlesFieldBeforeCategoryEscapesLabelFallbackAndLargePayloads()
        {
            const string userOnlyUuid = "useronly000000000000000001";
            const string escapedPassUuid = "escapedpass000000000000001";
            string largeNotes = new string('n', 8192);
            string json = $$"""
                {
                  "notes": "{{largeNotes}}",
                  "fields": [
                    { "value": "DecoyUser", "id": "username" },
                    { "value": "P@ss\"With\\Escapes\u0026More!", "label": "PASSWORD", "id": "custom-id" }
                  ],
                  "category": "login"
                }
                """;

            using var harness = CreateVaultHarness(
                new Dictionary<string, string>
                {
                    [userOnlyUuid] = $$"""{"id":"{{userOnlyUuid}}","category":"LOGIN","fields":[{"id":"username","value":"svc_escaped"}]}""",
                    [escapedPassUuid] = json
                },
                userNameItemId: userOnlyUuid,
                passwordItemId: escapedPassUuid);

            using var credential = harness.Source.Resolve(DeploymentTier.Production);

            Assert.IsNotNull(credential);
            Assert.AreEqual("svc_escaped", credential.UserName);
            Assert.AreEqual("P@ss\"With\\Escapes&More!", ReadSecureString(credential.Password));
        }

        [TestMethod]
        public void ParsePrincipalIdentifierFromUtf8Json_ExtractsSharedLoginPrincipalAndSkipsConcealedFields()
        {
            string largePadding = new string('x', 8192);
            string sharedLoginJson = $$"""
                {
                  "notes": "{{largePadding}}",
                  "fields": [
                    { "id": "password", "type": "CONCEALED", "purpose": "PASSWORD", "label": "password", "value": "NeverMaterializeThisSecret!99" },
                    { "id": "username", "type": "CONCEALED", "label": "username", "value": "ConcealedFakePrincipal" },
                    { "id": "custom-user", "type": "STRING", "purpose": "USERNAME", "label": "Service Login", "value": "CORP\\svc_shared_login" }
                  ],
                  "category": "LOGIN"
                }
                """;

            string principal = OnePasswordClient.ParsePrincipalIdentifierFromUtf8Json(
                Encoding.UTF8.GetBytes(sharedLoginJson),
                allowFallbackField: false);

            Assert.AreEqual(@"CORP\svc_shared_login", principal);

            const string concealedOnlyLoginJson = """
                {
                  "category": "LOGIN",
                  "fields": [
                    { "id": "username", "type": "CONCEALED", "value": "ConcealedSecretInUsernameSlot" },
                    { "id": "password", "type": "CONCEALED", "purpose": "PASSWORD", "value": "ActualPassword!123" }
                  ]
                }
                """;

            string rejectedConcealed = OnePasswordClient.ParsePrincipalIdentifierFromUtf8Json(
                Encoding.UTF8.GetBytes(concealedOnlyLoginJson),
                allowFallbackField: true);

            Assert.AreEqual(string.Empty, rejectedConcealed);
        }

        [TestMethod]
        public async Task StalledResponseBodyStream_CancelsWithinConfiguredHttpClientTimeout()
        {
            const string stalledUuid = "stalleditemuuid00000000001";
            using var handler = new StalledBodyHttpMessageHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://onepassword.corp.local"),
                Timeout = TimeSpan.FromMilliseconds(120)
            };
            var client = new OnePasswordClient(httpClient);

            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
                client.GetSecureSecretAsync("vault-1", stalledUuid)).ConfigureAwait(false);

            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
                client.GetPrincipalIdentifierAsync("vault-1", stalledUuid, allowFallbackField: false)).ConfigureAwait(false);

            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
                client.GetSecureSecretAsync("vault-1", "stalled-item-title")).ConfigureAwait(false);

            var configuration = Substitute.For<IConfigurationSettings>();
            configuration.GetOnePasswordVaultId().Returns("vault-1");
            var reader = new OnePasswordSecretsReader(
                configuration,
                client,
                Substitute.For<ILogger<OnePasswordSecretsReader>>());

            using SecureString timedOutSecret = reader.GetSecureSecret(stalledUuid, "Production deployment verification");
            string timedOutPrincipal = reader.GetPrincipalIdentifierByItemId(
                stalledUuid,
                "Production deployment username");

            Assert.AreEqual(0, timedOutSecret.Length);
            Assert.AreEqual(string.Empty, timedOutPrincipal);
        }

        [TestMethod]
        public void NamedIdentityByTitle_FiltersVaultItemsByExactTitleThenFetchesFullItemsByUuid()
        {
            const string userItemJson = $$"""
                {
                  "id": "{{TradingUserUuid}}",
                  "category": "API_CREDENTIAL",
                  "title": "trading-tier-username",
                  "fields": [
                    { "id": "credential", "value": "CORP\\svc-trading" }
                  ]
                }
                """;
            const string passItemJson = $$"""
                {
                  "id": "{{TradingPassUuid}}",
                  "category": "API_CREDENTIAL",
                  "title": "trading-tier-password",
                  "fields": [
                    { "id": "credential", "value": "TradingSecret!77" }
                  ]
                }
                """;

            using var harness = CreateVaultHarness(
                new Dictionary<string, string>
                {
                    [TradingUserUuid] = userItemJson,
                    [TradingPassUuid] = passItemJson
                },
                userNameItemId: string.Empty,
                passwordItemId: string.Empty);

            using var credential = harness.Source.Resolve(DeploymentTier.Production, "trading-tier");

            Assert.IsNotNull(credential);
            Assert.AreEqual(@"CORP\svc-trading", credential.UserName);
            Assert.AreEqual("TradingSecret!77", ReadSecureString(credential.Password));
            CollectionAssert.AreEqual(
                new[]
                {
                    "/v1/vaults/vault-1/items?filter=title%20eq%20%22trading-tier-username%22",
                    $"/v1/vaults/vault-1/items/{TradingUserUuid}",
                    "/v1/vaults/vault-1/items?filter=title%20eq%20%22trading-tier-password%22",
                    $"/v1/vaults/vault-1/items/{TradingPassUuid}"
                },
                harness.Handler.RequestedPathsAndQueries.ToArray());
        }

        [TestMethod]
        public async Task TitleLookup_RefusesAmbiguousDuplicateTitlesFailClosed()
        {
            const string userItemJson = $$"""
                {
                  "id": "{{TradingUserUuid}}",
                  "category": "API_CREDENTIAL",
                  "title": "trading-tier-username",
                  "fields": [
                    { "id": "credential", "value": "CORP\\svc-trading" }
                  ]
                }
                """;
            const string firstPassJson = $$"""
                {
                  "id": "{{TradingPassUuid}}",
                  "category": "API_CREDENTIAL",
                  "title": "trading-tier-password",
                  "fields": [
                    { "id": "credential", "value": "FirstSecret" }
                  ]
                }
                """;
            const string duplicatePassJson = $$"""
                {
                  "id": "{{DuplicatePassUuid}}",
                  "category": "API_CREDENTIAL",
                  "title": "trading-tier-password",
                  "fields": [
                    { "id": "credential", "value": "SecondSecret" }
                  ]
                }
                """;

            var items = new Dictionary<string, string>
            {
                [TradingUserUuid] = userItemJson,
                [TradingPassUuid] = firstPassJson,
                [DuplicatePassUuid] = duplicatePassJson
            };

            using var handler = new CannedVaultHttpMessageHandler(items);
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://onepassword.corp.local")
            };
            var client = new OnePasswordClient(httpClient);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                client.GetItemAsync("vault-1", "trading-tier-password"));

            using var harness = CreateVaultHarness(items, string.Empty, string.Empty);
            Assert.IsNull(harness.Source.Resolve(DeploymentTier.Production, "trading-tier"));
        }

        [TestMethod]
        public async Task TitleLookup_EnforcesOrdinalTitleMatchAndEscapesSpecialCharacters()
        {
            const string caseMismatchPassJson = $$"""
                {
                  "id": "{{TradingPassUuid}}",
                  "category": "API_CREDENTIAL",
                  "title": "Trading-Tier-Password",
                  "fields": [
                    { "id": "credential", "value": "WrongCaseSecret" }
                  ]
                }
                """;
            const string escapedTitleJson = $$"""
                {
                  "id": "{{EscapedTitleUuid}}",
                  "category": "API_CREDENTIAL",
                  "title": "corp\\svc-\"quoted\"",
                  "fields": [
                    { "id": "credential", "value": "EscapedSecret!11" }
                  ]
                }
                """;

            using var handler = new CannedVaultHttpMessageHandler(new Dictionary<string, string>
            {
                [TradingPassUuid] = caseMismatchPassJson,
                [EscapedTitleUuid] = escapedTitleJson
            });
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://onepassword.corp.local")
            };
            var client = new OnePasswordClient(httpClient);

            Assert.IsNull(await client.GetItemAsync("vault-1", "trading-tier-password"));
            Assert.IsNull(await client.GetItemAsync("vault-1", "missing-item-title"));

            var escapedItem = await client.GetItemAsync("vault-1", "corp\\svc-\"quoted\"");
            Assert.IsNotNull(escapedItem);
            Assert.AreEqual("EscapedSecret!11", escapedItem.GetFieldValue("credential"));
        }

        private static VaultTestHarness CreateVaultHarness(
            IReadOnlyDictionary<string, string> itemsById,
            string userNameItemId,
            string passwordItemId)
        {
            var configuration = Substitute.For<IConfigurationSettings>();
            configuration.GetOnePasswordVaultId().Returns("vault-1");
            configuration.GetDeploymentCredentialItemId(DeploymentTier.Production, false).Returns(userNameItemId);
            configuration.GetDeploymentCredentialItemId(DeploymentTier.Production, true).Returns(passwordItemId);

            var handler = new CannedVaultHttpMessageHandler(itemsById);
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://onepassword.corp.local")
            };
            var client = new OnePasswordClient(httpClient);
            var reader = new OnePasswordSecretsReader(
                configuration,
                client,
                Substitute.For<ILogger<OnePasswordSecretsReader>>());

            var source = new VaultDeploymentCredentialSource(
                reader,
                configuration,
                Substitute.For<ILogger<VaultDeploymentCredentialSource>>());

            return new VaultTestHarness(source, httpClient, handler);
        }

        private static string ReadSecureString(SecureString secret)
        {
            IntPtr bstr = IntPtr.Zero;
            try
            {
                bstr = Marshal.SecureStringToBSTR(secret);
                return Marshal.PtrToStringBSTR(bstr);
            }
            finally
            {
                if (bstr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeBSTR(bstr);
                }
            }
        }

        private sealed class VaultTestHarness : IDisposable
        {
            private readonly HttpClient _httpClient;
            private readonly CannedVaultHttpMessageHandler _handler;

            public VaultTestHarness(
                VaultDeploymentCredentialSource source,
                HttpClient httpClient,
                CannedVaultHttpMessageHandler handler)
            {
                Source = source;
                _httpClient = httpClient;
                _handler = handler;
            }

            public VaultDeploymentCredentialSource Source { get; }

            public CannedVaultHttpMessageHandler Handler => _handler;

            public void Dispose()
            {
                _httpClient.Dispose();
                _handler.Dispose();
            }
        }

        private sealed class CannedVaultHttpMessageHandler : HttpMessageHandler
        {
            private readonly IReadOnlyDictionary<string, string> _itemsById;
            private readonly List<string> _requestedPathsAndQueries = new();
            private readonly List<HttpResponseMessage> _createdResponses = new();
            private readonly List<HttpContent> _createdContents = new();

            public CannedVaultHttpMessageHandler(IReadOnlyDictionary<string, string> itemsById)
            {
                _itemsById = itemsById;
            }

            public IReadOnlyList<string> RequestedPathsAndQueries => _requestedPathsAndQueries;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(CreateResponse(request));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    foreach (HttpResponseMessage response in _createdResponses)
                    {
                        response.Dispose();
                    }

                    _createdResponses.Clear();

                    foreach (HttpContent content in _createdContents)
                    {
                        content.Dispose();
                    }

                    _createdContents.Clear();
                }

                base.Dispose(disposing);
            }

            private HttpResponseMessage CreateResponse(HttpRequestMessage request)
            {
                string path = request.RequestUri?.AbsolutePath ?? string.Empty;
                string query = request.RequestUri?.Query ?? string.Empty;
                _requestedPathsAndQueries.Add(path + query);

                string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

                if (segments.Length == 4
                    && segments[0] == "v1"
                    && segments[1] == "vaults"
                    && segments[3] == "items"
                    && query.StartsWith("?filter=", StringComparison.Ordinal))
                {
                    string filter = Uri.UnescapeDataString(query["?filter=".Length..]);
                    const string prefix = "title eq \"";
                    if (!filter.StartsWith(prefix, StringComparison.Ordinal)
                        || !filter.EndsWith("\"", StringComparison.Ordinal))
                    {
                        return CreateTrackedResponse(HttpStatusCode.BadRequest);
                    }

                    string rawEscapedTitle = filter.Substring(
                        prefix.Length,
                        filter.Length - prefix.Length - 1);
                    string requestedTitle = UnescapeFilterLiteral(rawEscapedTitle);

                    var summaries = new List<object>();
                    foreach (string itemJson in _itemsById.Values)
                    {
                        var parsed = System.Text.Json.JsonSerializer.Deserialize<OnePasswordItem>(itemJson);
                        if (parsed != null
                            && string.Equals(parsed.Title, requestedTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            // 1Password Connect list endpoint returns item summaries without fields.
                            summaries.Add(new
                            {
                                id = parsed.Id,
                                title = parsed.Title,
                                category = parsed.Category
                            });
                        }
                    }

                    string summariesJson = System.Text.Json.JsonSerializer.Serialize(summaries);
                    return CreateTrackedResponse(HttpStatusCode.OK, summariesJson);
                }

                if (segments.Length == 5
                    && segments[0] == "v1"
                    && segments[1] == "vaults"
                    && segments[3] == "items")
                {
                    string itemId = Uri.UnescapeDataString(segments[4]);
                    bool isValidUuid = itemId.Length == 26
                        && itemId.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'));
                    if (!isValidUuid)
                    {
                        return CreateTrackedResponse(HttpStatusCode.BadRequest);
                    }

                    if (_itemsById.TryGetValue(itemId, out string? json))
                    {
                        return CreateTrackedResponse(HttpStatusCode.OK, json);
                    }

                    return CreateTrackedResponse(HttpStatusCode.NotFound);
                }

                return CreateTrackedResponse(HttpStatusCode.NotFound);
            }

            private HttpResponseMessage CreateTrackedResponse(HttpStatusCode statusCode, string? json = null)
            {
                HttpContent? content = json is not null ? CreateTrackedContent(json) : null;
                var response = new HttpResponseMessage(statusCode);
                if (content is not null)
                {
                    response.Content = content;
                }

                _createdResponses.Add(response);
                return response;
            }

            private StringContent CreateTrackedContent(string json)
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                _createdContents.Add(content);
                return content;
            }

            private static string UnescapeFilterLiteral(string escaped)
            {
                var sb = new StringBuilder(escaped.Length);
                for (int i = 0; i < escaped.Length; i++)
                {
                    if (escaped[i] == '\\' && i + 1 < escaped.Length)
                    {
                        sb.Append(escaped[++i]);
                    }
                    else
                    {
                        sb.Append(escaped[i]);
                    }
                }

                return sb.ToString();
            }
        }

        private sealed class StalledBodyHttpMessageHandler : HttpMessageHandler
        {
            private readonly List<IDisposable> _disposables = new();

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(CreateStalledResponse());
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    foreach (IDisposable disposable in _disposables)
                    {
                        disposable.Dispose();
                    }

                    _disposables.Clear();
                }

                base.Dispose(disposing);
            }

            private HttpResponseMessage CreateStalledResponse()
            {
                var stream = Track(new StalledReadStream());
                var content = Track(new StreamContent(stream));
                var response = Track(new HttpResponseMessage(HttpStatusCode.OK));
                response.Content = content;
                return response;
            }

            private T Track<T>(T resource) where T : IDisposable
            {
                _disposables.Add(resource);
                return resource;
            }
        }

        private sealed class StalledReadStream : Stream
        {
            private static readonly byte[] InitialChunk = Encoding.UTF8.GetBytes("{\"category\":\"LOGIN\",\"fields\":[");
            private bool _sentInitialChunk;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                if (!_sentInitialChunk)
                {
                    _sentInitialChunk = true;
                    InitialChunk.AsMemory().CopyTo(buffer);
                    return InitialChunk.Length;
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return 0;
            }

            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();

            public override void SetLength(long value) =>
                throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();
        }
    }

    [TestClass]
    public class DeploymentCredentialConfigurationSettingsTests
    {
        [TestMethod]
        [DataRow(null, false)]
        [DataRow("", false)]
        [DataRow("   ", false)]
        [DataRow("false", false)]
        [DataRow("False", false)]
        [DataRow(" FALSE ", false)]
        [DataRow("true", true)]
        [DataRow("True", true)]
        [DataRow(" TRUE ", true)]
        public void ReadsVaultSelectionFlagFromConfiguration(string? rawValue, bool expected)
        {
            var values = new Dictionary<string, string?>();
            if (rawValue is not null)
            {
                values["AppSettings:DeploymentCredentialsFromVault"] = rawValue;
            }

            var settings = new ConfigurationSettings(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .AddInMemoryCollection(values)
                    .Build());

            Assert.AreEqual(expected, settings.GetDeploymentCredentialsFromVault());
        }

        [TestMethod]
        [DataRow("1")]
        [DataRow("0")]
        [DataRow("yes")]
        [DataRow("vault")]
        [DataRow("ture")]
        [DataRow("[PLACEHOLDER]")]
        [DataRow("[DORC.DEPLOYMENT.CREDENTIALS.FROM.VAULT]")]
        public void ThrowsInvalidOperationExceptionForUnrecognizedVaultSelectionValue(string invalidValue)
        {
            var settings = new ConfigurationSettings(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AppSettings:DeploymentCredentialsFromVault"] = invalidValue
                    })
                    .Build());

            Assert.ThrowsExactly<InvalidOperationException>(() => settings.GetDeploymentCredentialsFromVault());
        }

        [TestMethod]
        public void BindsAllFourVaultItemIdentifiersByTierAndComponent()
        {
            var settings = new ConfigurationSettings(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AppSettings:ProdDeployUsernameItemId"] = "prod-user-id",
                        ["AppSettings:ProdDeployPasswordItemId"] = "prod-pass-id",
                        ["AppSettings:NonProdDeployUsernameItemId"] = "nonprod-user-id",
                        ["AppSettings:NonProdDeployPasswordItemId"] = "nonprod-pass-id"
                    })
                    .Build());

            Assert.AreEqual("prod-user-id", settings.GetDeploymentCredentialItemId(DeploymentTier.Production, false));
            Assert.AreEqual("prod-pass-id", settings.GetDeploymentCredentialItemId(DeploymentTier.Production, true));
            Assert.AreEqual("nonprod-user-id", settings.GetDeploymentCredentialItemId(DeploymentTier.NonProduction, false));
            Assert.AreEqual("nonprod-pass-id", settings.GetDeploymentCredentialItemId(DeploymentTier.NonProduction, true));
        }
    }

    [TestClass]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class DaemonStatusProbeCredentialResolutionTests
    {
        [TestMethod]
        [DataRow(true, DeploymentTier.Production)]
        [DataRow(false, DeploymentTier.NonProduction)]
        public void DaemonProbeResolvesTierThroughCredentialSourceAndRefusesHostFallbackWhenNull(
            bool environmentIsProd,
            DeploymentTier expectedTier)
        {
            var configValues = Substitute.For<IConfigValuesPersistentSource>();
            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            var servers = Substitute.For<IServersPersistentSource>();
            var daemons = Substitute.For<IDaemonsPersistentSource>();
            var settings = Substitute.For<IConfigurationSettings>();
            settings.GetConfigurationDomainNameIntra().Returns("CORP");

            var env = new Dorc.ApiModel.EnvironmentApiModel
            {
                EnvironmentId = 17,
                EnvironmentName = environmentIsProd ? "PROD-EU" : "DEV-EU",
                EnvironmentIsProd = environmentIsProd
            };
            environments.GetEnvironment(17, null).Returns(env);
            environments.GetEnvironment(env.EnvironmentName, Arg.Any<System.Security.Claims.ClaimsPrincipal>()).Returns(env);
            servers.GetServersForEnvId(17).Returns(new[]
            {
                new Dorc.ApiModel.ServerApiModel { ServerId = 3, Name = "127.0.0.1" }
            });
            daemons.GetDaemonsForServer(3).Returns(new[]
            {
                new Dorc.ApiModel.DaemonApiModel { Id = 9, Name = "EventLog" }
            });
            daemons.GetDaemons().Returns(new[]
            {
                new Dorc.ApiModel.DaemonApiModel { Id = 9, Name = "EventLog" }
            });

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            credentialSource.Description.Returns("test credential source");
            credentialSource.Resolve(Arg.Any<DeploymentTier>(), Arg.Any<string?>()).Returns((DeploymentCredential?)null);

            var probe = new DaemonStatusProbe(
                configValues,
                Substitute.For<ILogger<DaemonStatusProbe>>(),
                environments,
                servers,
                daemons,
                Substitute.For<IDaemonObservationPersistentSource>(),
                settings,
                Substitute.For<IDaemonAuditPersistentSource>(),
                Substitute.For<IServersAuditPersistentSource>(),
                Substitute.For<Dorc.PersistentData.IClaimsPrincipalReader>(),
                credentialSource);

            var principal = new System.Security.Claims.ClaimsPrincipal();

            var statusList = probe.GetDaemonStatuses(17);
            var discoveryResult = probe.DiscoverAndMapDaemons(env.EnvironmentName, principal);
            var rawDiscoveryList = probe.DiscoverAllDaemonsForEnvironment(env.EnvironmentName, principal);
            var stateChangeResult = probe.ChangeDaemonState(
                new DaemonStatus
                {
                    EnvName = env.EnvironmentName,
                    ServerName = "127.0.0.1",
                    DaemonName = "EventLog",
                    Status = "Starting"
                },
                principal);

            credentialSource.Received(4).Resolve(expectedTier, null);
            configValues.DidNotReceiveWithAnyArgs().GetConfigValue(default!);

            Assert.AreEqual(1, statusList.Count);
            Assert.IsNull(statusList[0].Status, "GetDaemonStatuses must not probe as the host account when credential is null.");
            StringAssert.Contains(statusList[0].ErrorMessage, "No deployment credential is available");
            Assert.IsFalse(discoveryResult.Success, "DiscoverAndMapDaemons must not report Success when credentials are missing.");
            Assert.AreEqual(0, discoveryResult.ServersProcessed, "DiscoverAndMapDaemons must not report processed servers when no probe ran.");
            Assert.AreEqual(0, discoveryResult.DaemonsDiscovered, "DiscoverAndMapDaemons must not probe as the host account when credential is null.");
            Assert.AreEqual(1, discoveryResult.Errors.Count);
            Assert.AreEqual(0, rawDiscoveryList.Count, "DiscoverAllDaemonsForEnvironment must not return unprobed candidate daemons when credential is null.");
            Assert.IsNull(stateChangeResult);
        }

        [TestMethod]
        public void ProbeMethods_PassBoundExecutionIdentityReferenceAndFailClosedWhenUnresolvable()
        {
            var settings = Substitute.For<IConfigurationSettings>();
            settings.GetConfigurationDomainNameIntra().Returns("CORP");

            var env = new Dorc.ApiModel.EnvironmentApiModel
            {
                EnvironmentId = 17,
                EnvironmentName = "PAYMENTS-PROD",
                EnvironmentIsProd = true,
                ExecutionIdentityReference = "payments-prod"
            };

            var environments = Substitute.For<IEnvironmentsPersistentSource>();
            environments.GetEnvironment(17, null).Returns(env);
            environments.GetEnvironment(env.EnvironmentName).Returns(env);
            environments.GetEnvironment(env.EnvironmentName, Arg.Any<System.Security.Claims.ClaimsPrincipal>()).Returns(env);

            var servers = Substitute.For<IServersPersistentSource>();
            servers.GetServersForEnvId(17).Returns(new[]
            {
                new Dorc.ApiModel.ServerApiModel { ServerId = 3, Name = "127.0.0.1" }
            });
            var daemons = Substitute.For<IDaemonsPersistentSource>();
            daemons.GetDaemonsForServer(3).Returns(new[]
            {
                new Dorc.ApiModel.DaemonApiModel { Id = 9, Name = "EventLog" }
            });
            daemons.GetDaemons().Returns(new[]
            {
                new Dorc.ApiModel.DaemonApiModel { Id = 9, Name = "EventLog" }
            });

            var credentialSource = Substitute.For<IDeploymentCredentialSource>();
            credentialSource.Description.Returns("vault");
            credentialSource.Resolve(DeploymentTier.Production, null)
                .Returns(_ => DeploymentCredential.FromPlainText("svc-shared-prod", "shared-secret"));
            credentialSource.Resolve(DeploymentTier.Production, "payments-prod")
                .Returns((DeploymentCredential?)null);

            var probe = new DaemonStatusProbe(
                Substitute.For<IConfigValuesPersistentSource>(),
                Substitute.For<ILogger<DaemonStatusProbe>>(),
                environments,
                servers,
                daemons,
                Substitute.For<IDaemonObservationPersistentSource>(),
                settings,
                Substitute.For<IDaemonAuditPersistentSource>(),
                Substitute.For<IServersAuditPersistentSource>(),
                Substitute.For<Dorc.PersistentData.IClaimsPrincipalReader>(),
                credentialSource);

            var statusList = probe.GetDaemonStatuses(17);

            credentialSource.Received(1).Resolve(DeploymentTier.Production, "payments-prod");
            credentialSource.DidNotReceive().Resolve(DeploymentTier.Production, null);
            Assert.AreEqual(1, statusList.Count);
            Assert.IsNull(statusList[0].Status);
        }
    }

    [TestClass]
    public class SqlUserPasswordResetSecurityTests
    {
        [TestMethod]
        [DataRow("sql01.corp.local")]
        [DataRow(@"sql01.corp.local\INST01")]
        [DataRow("sql01.corp.local,1433")]
        public void BuildConnectionString_UsesSqlConnectionStringBuilderForValidServer(string serverName)
        {
            string connectionString = SqlUserPasswordReset.BuildConnectionString(serverName);

            var parsed = new SqlConnectionStringBuilder(connectionString);
            Assert.AreEqual(serverName, parsed.DataSource);
            Assert.AreEqual("master", parsed.InitialCatalog);
            Assert.IsTrue(parsed.IntegratedSecurity);
            Assert.IsTrue(parsed.TrustServerCertificate);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("sql01;Initial Catalog=evil;Integrated Security=False")]
        [DataRow("sql01 .corp.local")]
        [DataRow("sql01\r\n.corp.local")]
        [DataRow("sql01'evil")]
        public void BuildConnectionString_RejectsConnectionStringInjectionAndMalformedServers(string maliciousServer)
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                SqlUserPasswordReset.BuildConnectionString(maliciousServer));
        }

        [TestMethod]
        public void BuildAlterLoginStatement_QuotesIdentifierAndLiteralForValidSqlLogin()
        {
            string statement = SqlUserPasswordReset.BuildAlterLoginStatement("app_user-01.svc");

            Assert.AreEqual(
                "ALTER LOGIN [app_user-01.svc] WITH PASSWORD = N'app_user-01.svc'",
                statement);
        }

        [TestMethod]
        public void CreateAlterLoginCommand_UsesConstantParameterizedSqlWithQuotenameParameters()
        {
            using var connection = new SqlConnection();
            using var command = SqlUserPasswordReset.CreateAlterLoginCommand(connection, "app_user-01.svc");

            Assert.AreSame(SqlUserPasswordReset.AlterLoginParameterizedSql, command.CommandText);
            Assert.IsFalse(command.CommandText.Contains("app_user-01.svc", StringComparison.Ordinal));
            Assert.AreEqual(2, command.Parameters.Count);

            var loginParam = command.Parameters["@loginName"];
            Assert.AreEqual(SqlDbType.NVarChar, loginParam.SqlDbType);
            Assert.AreEqual(128, loginParam.Size);
            Assert.AreEqual("app_user-01.svc", loginParam.Value);

            var passwordParam = command.Parameters["@newPassword"];
            Assert.AreEqual(SqlDbType.NVarChar, passwordParam.SqlDbType);
            Assert.AreEqual(128, passwordParam.Size);
            Assert.AreEqual("app_user-01.svc", passwordParam.Value);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("app'] WITH CHECK_POLICY=OFF; --")]
        [DataRow("sa] ENABLE; --")]
        [DataRow("user;DROP LOGIN [sa]")]
        [DataRow("user name")]
        [DataRow("[app_user]")]
        public void BuildAlterLoginStatement_RejectsSqlInjectionPayloads(string maliciousUsername)
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                SqlUserPasswordReset.BuildAlterLoginStatement(maliciousUsername));

            using var connection = new SqlConnection();
            Assert.ThrowsExactly<ArgumentException>(() =>
                SqlUserPasswordReset.CreateAlterLoginCommand(connection, maliciousUsername));

            var reset = new SqlUserPasswordReset(Substitute.For<ILogger<SqlUserPasswordReset>>());
            var result = reset.ResetSqlUserPassword("sql01.corp.local", maliciousUsername);
            Assert.IsFalse(result.Result);
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
            using var credential = _source.Resolve(DeploymentTier.NonProduction, "trading-tier");

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
            using var credential = _source.Resolve(DeploymentTier.NonProduction, identityReference);

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
        public void WhitespaceOnlyNamedIdentityUsernameOrPasswordRefusesFailClosed()
        {
            _configValues.GetConfigValue("DORC_Deploy_blank-pass_Username").Returns("svc-blank");
            _configValues.GetConfigValue("DORC_Deploy_blank-pass_Password").Returns("   ");
            _configValues.GetConfigValue("DORC_Deploy_blank-user_Username").Returns("   ");
            _configValues.GetConfigValue("DORC_Deploy_blank-user_Password").Returns("secret");

            Assert.IsNull(_source.Resolve(DeploymentTier.NonProduction, "blank-pass"));
            Assert.IsNull(_source.Resolve(DeploymentTier.NonProduction, "blank-user"));
            Assert.AreEqual((0, 0), _source.ResolutionCounts);
        }

        [TestMethod]
        [DataRow("../secret")]
        [DataRow("payments\n")]
        [DataRow("payments\r\n")]
        [DataRow("pay\nments")]
        [DataRow("\npayments")]
        public void AnInvalidPersistedIdentityReferenceIsRefusedBeforeCredentialLookup(string invalidReference)
        {
            Assert.ThrowsExactly<ArgumentException>(() =>
                Dorc.ApiModel.EnvironmentExecutionIdentityReference.Validate(invalidReference));

            Assert.IsNull(_source.Resolve(DeploymentTier.NonProduction, invalidReference));

            _configValues.DidNotReceive().GetConfigValue($"DORC_Deploy_{invalidReference}_Username");
            _configValues.DidNotReceive().GetConfigValue($"DORC_Deploy_{invalidReference}_Password");
            Assert.AreEqual((0, 0), _source.ResolutionCounts);
        }

        [TestMethod]
        public void DuplicateConfigValueRowsAcrossIsForProdRefuseCleanlyWithoutThrowingOrLeakingSecrets()
        {
            const string superSecret = "SuperSecret!Pass#99";
            var stored = new List<Dorc.PersistentData.Model.ConfigValue>
            {
                new() { Id = 1, Key = "DORC_Deploy_payments_Username", Value = "svc-payments-prod", IsForProd = true },
                new() { Id = 2, Key = "DORC_Deploy_payments_Username", Value = "svc-payments-nonprod", IsForProd = false },
                new() { Id = 3, Key = "DORC_Deploy_payments_Password", Value = "enc-1", Secure = true, IsForProd = true },
                new() { Id = 4, Key = "DORC_Deploy_payments_Password", Value = "enc-2", Secure = true, IsForProd = false },
                new() { Id = 5, Key = "DORC_ProdDeployUsername", Value = "svc-prod-1", IsForProd = true },
                new() { Id = 6, Key = "DORC_ProdDeployUsername", Value = "svc-prod-2", IsForProd = false },
                new() { Id = 7, Key = "DORC_ProdDeployPassword", Value = "enc-prod", Secure = true, IsForProd = true }
            };

            var queryable = stored.AsQueryable();
            var dbSet = Substitute.For<Microsoft.EntityFrameworkCore.DbSet<Dorc.PersistentData.Model.ConfigValue>, IQueryable<Dorc.PersistentData.Model.ConfigValue>>();
            ((IQueryable<Dorc.PersistentData.Model.ConfigValue>)dbSet).Provider.Returns(queryable.Provider);
            ((IQueryable<Dorc.PersistentData.Model.ConfigValue>)dbSet).Expression.Returns(queryable.Expression);
            ((IQueryable<Dorc.PersistentData.Model.ConfigValue>)dbSet).ElementType.Returns(queryable.ElementType);
            ((IQueryable<Dorc.PersistentData.Model.ConfigValue>)dbSet).GetEnumerator().Returns(_ => queryable.GetEnumerator());

            var context = Substitute.For<Dorc.PersistentData.Contexts.IDeploymentContext>();
            context.ConfigValues.Returns(dbSet);
            var contextFactory = Substitute.For<Dorc.PersistentData.Contexts.IDeploymentContextFactory>();
            contextFactory.GetContext().Returns(context);

            var encryptor = Substitute.For<Dorc.PersistentData.IPropertyEncryptor>();
            encryptor.DecryptValue(Arg.Any<string>()).Returns(superSecret);

            var persistentSource = new Dorc.PersistentData.Sources.ConfigValuesPersistentSource(contextFactory, encryptor);
            var logger = Substitute.For<ILogger<ConfigValueDeploymentCredentialSource>>();
            var source = new ConfigValueDeploymentCredentialSource(persistentSource, logger);

            Assert.IsNull(source.Resolve(DeploymentTier.Production, "payments"));
            Assert.IsNull(source.Resolve(DeploymentTier.NonProduction, "payments"));
            Assert.IsNull(source.Resolve(DeploymentTier.Production, null));
            Assert.AreEqual((0, 0), source.ResolutionCounts);
            encryptor.DidNotReceive().DecryptValue("enc-1");
            encryptor.DidNotReceive().DecryptValue("enc-2");

            var formattedMessages = logger.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
                .Select(call => call.GetArguments()[2]?.ToString() ?? string.Empty)
                .ToList();
            var loggedMessages = logger.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
                .Select(call =>
                    $"{call.GetArguments()[2]} {call.GetArguments()[3]}")
                .ToList();
            Assert.IsTrue(formattedMessages.Any(m => m.Contains("username entry", StringComparison.Ordinal)));
            Assert.IsTrue(formattedMessages.Any(m => m.Contains("verification entry", StringComparison.Ordinal)));
            Assert.IsFalse(formattedMessages.Any(m => m.Contains("DORC_Deploy_payments_Password", StringComparison.Ordinal)));
            Assert.IsTrue(loggedMessages.Any(m => m.Contains("DORC_Deploy_payments_Username", StringComparison.Ordinal)));
            Assert.IsTrue(loggedMessages.Any(m => m.Contains("DORC_Deploy_payments_Password", StringComparison.Ordinal)));
            Assert.IsFalse(loggedMessages.Any(m => m.Contains(superSecret, StringComparison.Ordinal)));
        }

        /// <summary>
        /// "We have started binding identities" is not the same claim as "the estate is bound".
        /// Reported from the count rather than from a configuration flag, so it cannot be wrong.
        /// </summary>
        [TestMethod]
        public void CountsBoundResolutionsSeparatelyFromFallbacks()
        {
            using var boundCredential = _source.Resolve(DeploymentTier.NonProduction, "trading-tier");
            using var nullFallback = _source.Resolve(DeploymentTier.NonProduction, null);
            using var whitespaceFallback = _source.Resolve(DeploymentTier.NonProduction, "   ");

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

        [TestMethod]
        public void VaultNamedIdentityReadsUsernameViaGetPrincipalIdentifierByItemIdNotGetSecretByItemId()
        {
            var secrets = Substitute.For<IConfigurationSecretsReader>();
            secrets.GetPrincipalIdentifierByItemId("payments-prod-username", Arg.Any<string>())
                .Returns("  CORP\\svc-payments  ");
            secrets.GetSecureSecret("payments-prod-password", Arg.Any<string>())
                .Returns(_ => DeploymentCredential.ToSecureString("vault-payments-secret"));

            var vaultSource = new VaultDeploymentCredentialSource(
                secrets,
                Substitute.For<IConfigurationSettings>(),
                Substitute.For<ILogger<VaultDeploymentCredentialSource>>());

            using var credential = vaultSource.Resolve(DeploymentTier.Production, "payments-prod");

            Assert.IsNotNull(credential);
            Assert.AreEqual("CORP\\svc-payments", credential.UserName);
            Assert.AreEqual((1, 0), vaultSource.ResolutionCounts);
            secrets.Received(1).GetPrincipalIdentifierByItemId("payments-prod-username", Arg.Any<string>());
            secrets.Received(1).GetSecureSecret("payments-prod-password", Arg.Any<string>());
            secrets.DidNotReceiveWithAnyArgs().GetSecretByItemId(default!, default!);
        }

        [TestMethod]
        [DataRow("Not Found", "vault-secret")]
        [DataRow("   ", "vault-secret")]
        [DataRow("", "vault-secret")]
        [DataRow("svc-payments", "Not Found")]
        [DataRow("svc-payments", "   ")]
        [DataRow("svc-payments", "")]
        public void VaultNamedIdentityFailsClosedAndDisposesPasswordWhenEitherItemIsMissingOrSentinel(
            string returnedUserName,
            string returnedPassword)
        {
            var secrets = Substitute.For<IConfigurationSecretsReader>();
            var settings = Substitute.For<IConfigurationSettings>();
            settings.GetDeploymentCredentialItemId(DeploymentTier.Production, false).Returns("tier-prod-user");
            settings.GetDeploymentCredentialItemId(DeploymentTier.Production, true).Returns("tier-prod-pass");
            secrets.GetPrincipalIdentifierByItemId("tier-prod-user", Arg.Any<string>()).Returns("svc-tier-default");
            secrets.GetSecureSecret("tier-prod-pass", Arg.Any<string>())
                .Returns(_ => DeploymentCredential.ToSecureString("tier-default-secret"));

            var passwordSecret = DeploymentCredential.ToSecureString(returnedPassword);
            secrets.GetPrincipalIdentifierByItemId("payments-prod-username", Arg.Any<string>())
                .Returns(returnedUserName);
            secrets.GetSecureSecret("payments-prod-password", Arg.Any<string>())
                .Returns(passwordSecret);

            var vaultSource = new VaultDeploymentCredentialSource(
                secrets,
                settings,
                Substitute.For<ILogger<VaultDeploymentCredentialSource>>());

            var resolved = vaultSource.Resolve(DeploymentTier.Production, "payments-prod");

            Assert.IsNull(resolved, "Named identity must fail closed rather than falling back to tier default.");
            Assert.AreEqual((0, 0), vaultSource.ResolutionCounts);
            Assert.ThrowsExactly<ObjectDisposedException>(() => _ = passwordSecret.Length);
            secrets.DidNotReceive().GetPrincipalIdentifierByItemId("tier-prod-user", Arg.Any<string>());
            secrets.DidNotReceiveWithAnyArgs().GetSecretByItemId(default!, default!);
        }

        [TestMethod]
        [DataRow("svc-trading", "CORP", @"CORP\svc-trading")]
        [DataRow("  svc-trading  ", "  CORP  ", @"CORP\svc-trading")]
        [DataRow(@"OTHER\svc-payments", "CORP", @"OTHER\svc-payments")]
        [DataRow("svc-payments@corp.example", "CORP", "svc-payments@corp.example")]
        [DataRow("svc-trading", "", "svc-trading")]
        [DataRow("svc-trading", null, "svc-trading")]
        public void NamedIdentityCredentialQualifiesBareUserNameAndPreservesExplicitDomain(
            string configuredUserName,
            string? domain,
            string expectedQualifiedAccountName)
        {
            _configValues.GetConfigValue("DORC_Deploy_payments-env_Username").Returns(configuredUserName);
            _configValues.GetConfigValue("DORC_Deploy_payments-env_Password").Returns("payments-secret");

            var credential = _source.Resolve(DeploymentTier.Production, "payments-env");

            Assert.IsNotNull(credential);
            Assert.AreEqual(expectedQualifiedAccountName, credential.QualifiedAccountName(domain));
        }
    }
}

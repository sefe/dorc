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
        [TestMethod]
        public void LoginItem_ReadsUsernameIntoUserNameAndPasswordIntoPassword_NeverLeakingPasswordIntoUserName()
        {
            const string loginJson = """
                {
                  "id": "login-item-1",
                  "category": "LOGIN",
                  "title": "DOrc Prod Deploy",
                  "fields": [
                    { "id": "username", "type": "STRING", "label": "username", "value": "svc_dorc_prod" },
                    { "id": "password", "type": "CONCEALED", "label": "password", "value": "UltraSecret!Pass#99" }
                  ]
                }
                """;

            using var harness = CreateVaultHarness(
                new Dictionary<string, string> { ["login-item-1"] = loginJson },
                userNameItemId: "login-item-1",
                passwordItemId: "login-item-1");

            using var credential = harness.Source.Resolve(DeploymentTier.Production);

            Assert.IsNotNull(credential);
            Assert.AreEqual("svc_dorc_prod", credential.UserName);
            Assert.AreNotEqual("UltraSecret!Pass#99", credential.UserName);
            Assert.AreEqual("UltraSecret!Pass#99", ReadSecureString(credential.Password));
        }

        [TestMethod]
        [DataRow("""{"id":"login-1","category":"LOGIN","fields":[{"id":"password","value":"SecretOnly"}]}""")]
        [DataRow("""{"id":"login-1","category":"LOGIN","fields":[{"id":"username","value":"Not Found"},{"id":"password","value":"SecretOnly"}]}""")]
        [DataRow("""{"id":"login-1","category":"LOGIN","fields":[{"id":"username","value":"svc_dorc_prod"}]}""")]
        [DataRow("""{"id":"login-1","category":"LOGIN","fields":[{"id":"username","value":"svc_dorc_prod"},{"id":"password","value":"Not Found"}]}""")]
        public void LoginItem_FailsClosedWhenUsernameOrPasswordFieldIsMissingOrSentinel(string itemJson)
        {
            using var harness = CreateVaultHarness(
                new Dictionary<string, string> { ["login-1"] = itemJson },
                userNameItemId: "login-1",
                passwordItemId: "login-1");

            Assert.IsNull(harness.Source.Resolve(DeploymentTier.Production));
        }

        [TestMethod]
        public void SharedApiCredentialItemWithoutUsernameField_RefusesRatherThanPuttingCredentialInUserName()
        {
            const string apiCredJson = """
                {
                  "id": "shared-api-cred",
                  "category": "API_CREDENTIAL",
                  "fields": [
                    { "id": "username", "value": "" },
                    { "id": "credential", "value": "SharedApiSecretValue" }
                  ]
                }
                """;

            using var harness = CreateVaultHarness(
                new Dictionary<string, string> { ["shared-api-cred"] = apiCredJson },
                userNameItemId: "shared-api-cred",
                passwordItemId: "shared-api-cred");

            Assert.IsNull(harness.Source.Resolve(DeploymentTier.Production));
        }

        [TestMethod]
        public void SeparateApiCredentialItems_ResolvesDedicatedUsernameAndPasswordItems()
        {
            const string userItemJson = """
                {
                  "id": "user-api-cred",
                  "category": "API_CREDENTIAL",
                  "fields": [
                    { "id": "credential", "value": "svc_nonprod" },
                    { "id": "type", "value": "other" }
                  ]
                }
                """;
            const string passItemJson = """
                {
                  "id": "pass-api-cred",
                  "category": "API_CREDENTIAL",
                  "fields": [
                    { "id": "credential", "value": "NonProdSecret!42" }
                  ]
                }
                """;

            using var harness = CreateVaultHarness(
                new Dictionary<string, string>
                {
                    ["user-api-cred"] = userItemJson,
                    ["pass-api-cred"] = passItemJson
                },
                userNameItemId: "user-api-cred",
                passwordItemId: "pass-api-cred");

            using var credential = harness.Source.Resolve(DeploymentTier.Production);

            Assert.IsNotNull(credential);
            Assert.AreEqual("svc_nonprod", credential.UserName);
            Assert.AreEqual("NonProdSecret!42", ReadSecureString(credential.Password));
        }

        [TestMethod]
        public void ParseSecureSecretFromUtf8Json_HandlesFieldBeforeCategoryEscapesLabelFallbackAndLargePayloads()
        {
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
                    ["user-only"] = """{"id":"user-only","category":"LOGIN","fields":[{"id":"username","value":"svc_escaped"}]}""",
                    ["escaped-pass"] = json
                },
                userNameItemId: "user-only",
                passwordItemId: "escaped-pass");

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
            using var handler = new StalledBodyHttpMessageHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://onepassword.corp.local"),
                Timeout = TimeSpan.FromMilliseconds(120)
            };
            var client = new OnePasswordClient(httpClient);

            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
                client.GetSecureSecretAsync("vault-1", "stalled-item")).ConfigureAwait(false);

            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
                client.GetPrincipalIdentifierAsync("vault-1", "stalled-item", allowFallbackField: false)).ConfigureAwait(false);

            var configuration = Substitute.For<IConfigurationSettings>();
            configuration.GetOnePasswordVaultId().Returns("vault-1");
            var reader = new OnePasswordSecretsReader(
                configuration,
                client,
                Substitute.For<ILogger<OnePasswordSecretsReader>>());

            using SecureString timedOutSecret = reader.GetSecureSecret("stalled-item", "Production deployment verification");
            string timedOutPrincipal = reader.GetPrincipalIdentifierByItemId(
                "stalled-item",
                "Production deployment username");

            Assert.AreEqual(0, timedOutSecret.Length);
            Assert.AreEqual(string.Empty, timedOutPrincipal);
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

            public void Dispose()
            {
                _httpClient.Dispose();
                _handler.Dispose();
            }
        }

        private sealed class CannedVaultHttpMessageHandler : HttpMessageHandler
        {
            private readonly IReadOnlyDictionary<string, string> _itemsById;
            private readonly List<IDisposable> _disposables = new();

            public CannedVaultHttpMessageHandler(IReadOnlyDictionary<string, string> itemsById)
            {
                _itemsById = itemsById;
            }

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
                    foreach (IDisposable disposable in _disposables)
                    {
                        disposable.Dispose();
                    }

                    _disposables.Clear();
                }

                base.Dispose(disposing);
            }

            private HttpResponseMessage CreateResponse(HttpRequestMessage request)
            {
                string path = request.RequestUri?.AbsolutePath ?? string.Empty;
                string itemId = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;

                if (_itemsById.TryGetValue(itemId, out string? json))
                {
                    var content = Track(new StringContent(json, Encoding.UTF8, "application/json"));
                    var okResponse = Track(new HttpResponseMessage(HttpStatusCode.OK));
                    okResponse.Content = content;
                    return okResponse;
                }

                return Track(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            private T Track<T>(T resource) where T : IDisposable
            {
                _disposables.Add(resource);
                return resource;
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
            credentialSource.Resolve(Arg.Any<DeploymentTier>()).Returns((DeploymentCredential?)null);

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

            credentialSource.Received(4).Resolve(expectedTier);
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
}

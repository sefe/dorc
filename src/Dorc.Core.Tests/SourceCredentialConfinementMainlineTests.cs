using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Dorc.ApiModel;
using Dorc.AzureDevOps.Client;
using Dorc.AzureDevOps.Client.Auth;
using Dorc.Core.AzureDevOpsServer;
using Dorc.Core.BuildServer;
using Dorc.Core.Interfaces;
using Dorc.Core.Security;
using Dorc.PersistentData;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Security;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.Runner.Logger;
using Dorc.Terraform.Catalog;
using Dorc.TerraformRunner.CodeSources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dorc.Core.Tests
{
    [TestClass]
    public class SourceCredentialConfinementMainlineTests
    {
        [TestMethod]
        [DataRow("https://dev.azure.com/myorg/", true)]
        [DataRow("https://vsrm.dev.azure.com/myorg/", true)]
        [DataRow("https://myorg.visualstudio.com/", true)]
        [DataRow("https://dev.azure.com.attacker.net/myorg/", false)]
        [DataRow("https://attacker.net/dev.azure.com/", false)]
        [DataRow("https://dev.azure.com@attacker.net/myorg/", false)]
        [DataRow("https://tfs.corp.internal/tfs/DefaultCollection/", false)]
        public void AzureDevOpsEndpointClassificationUsesParsedAuthority(string endpoint, bool expected)
        {
            var client = CreateAzureDevOpsClient(BuildAllowList());
            Assert.AreEqual(expected, client.IsConfiguredAzureEndpoint(endpoint), endpoint);
        }

        [TestMethod]
        [DataRow("https://tfs.corp.internal/tfs/DefaultCollection/_apis/build/builds/1/artifacts")]
        [DataRow(@"\\fileserver.corp.internal\drops\build1")]
        public async Task AzureDevOpsDefaultWindowsCredentialsFailClosedWhenAllowListUnconfigured(string url)
        {
            var handlerCalled = false;
            var client = CreateAzureDevOpsClient(
                BuildAllowList(),
                _ => new RecordingHttpHandler(_ =>
                {
                    handlerCalled = true;
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }));

            var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                client.TryReadArtifactFileTextAsync(url, "bundle.json"));

            StringAssert.Contains(refusal.Message, SourceHostAllowList.ArtefactHostsSetting);
            Assert.IsFalse(handlerCalled, "No outbound connection should occur when default credentials are refused.");
        }

        [TestMethod]
        public async Task AzureDevOpsDefaultWindowsCredentialsFailClosedWhenHostOrRedirectTargetIsDisallowed()
        {
            var allowList = BuildAllowList(artefactHosts: ["tfs.corp.internal"]);
            var requestedUrls = new List<string>();
            var client = CreateAzureDevOpsClient(
                allowList,
                _ => new RecordingHttpHandler(req =>
                {
                    requestedUrls.Add(req.RequestUri!.AbsoluteUri);
                    var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri("https://attacker.internal/harvest-ntlm");
                    return redirect;
                }),
                serverUrl: "https://tfs.corp.internal/tfs/DefaultCollection");

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                client.TryReadArtifactFileTextAsync("https://other.corp.internal/drop.zip", "bundle.json"));
            Assert.AreEqual(0, requestedUrls.Count);

            var redirectRefusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                client.TryReadArtifactFileTextAsync("https://tfs.corp.internal/drop.zip", "bundle.json"));
            StringAssert.Contains(redirectRefusal.Message, "attacker.internal");
            Assert.AreEqual(1, requestedUrls.Count, "Redirect target outside allow-list must never be contacted.");
        }

        [TestMethod]
        [DataRow("https://other-allowed.corp.internal/drop.zip", "configured endpoint origin")]
        [DataRow("http://tfs.corp.internal/drop.zip", "HTTPS-to-http redirect")]
        [DataRow("https://tfs.corp.internal:8443/drop.zip", "configured endpoint origin")]
        public async Task AzureDevOpsDefaultWindowsCredentialsLockRedirectsToConfiguredOrigin(string redirectTarget, string expectedReason)
        {
            var allowList = BuildAllowList(artefactHosts: ["tfs.corp.internal", "other-allowed.corp.internal"]);
            var requestedUrls = new List<string>();
            var client = CreateAzureDevOpsClient(
                allowList,
                _ => new RecordingHttpHandler(req =>
                {
                    requestedUrls.Add(req.RequestUri!.AbsoluteUri);
                    var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri(redirectTarget);
                    return redirect;
                }),
                serverUrl: "https://tfs.corp.internal/tfs/DefaultCollection");

            var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                client.TryReadArtifactFileTextAsync("https://tfs.corp.internal/tfs/DefaultCollection/drop.zip", "bundle.json"));

            StringAssert.Contains(refusal.Message, expectedReason);
            Assert.AreEqual(1, requestedUrls.Count, $"Cross-origin redirect '{redirectTarget}' must never be contacted with default credentials.");
        }

        [TestMethod]
        public async Task AzureDevOpsDefaultWindowsCredentialsAllowSameOriginRedirect()
        {
            var allowList = BuildAllowList(artefactHosts: ["tfs.corp.internal"]);
            var requestedUrls = new List<string>();
            var zipBytes = BuildZipWithEntry("bundle.json", "{\"ok\":true}");
            var client = CreateAzureDevOpsClient(
                allowList,
                _ => new RecordingHttpHandler(req =>
                {
                    requestedUrls.Add(req.RequestUri!.AbsoluteUri);
                    if (req.RequestUri.AbsolutePath.EndsWith("/initial.zip", StringComparison.Ordinal))
                    {
                        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                        redirect.Headers.Location = new Uri("https://tfs.corp.internal:443/tfs/DefaultCollection/final.zip");
                        return redirect;
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(zipBytes)
                    };
                }),
                serverUrl: "https://tfs.corp.internal/tfs/DefaultCollection");

            var content = await client.TryReadArtifactFileTextAsync(
                "https://tfs.corp.internal/tfs/DefaultCollection/initial.zip",
                "bundle.json");

            Assert.AreEqual("{\"ok\":true}", content);
            Assert.AreEqual(2, requestedUrls.Count);
        }

        [TestMethod]
        public async Task AzureDevOpsBundleReadRefusesMissingOrCrossOriginProjectServerEvenWhenDownloadHostIsAllowListed()
        {
            var allowList = BuildAllowList(artefactHosts: ["tfs-project.corp.internal", "tfs-other.corp.internal"]);
            var handlerCalled = false;
            var loggerFactory = Substitute.For<ILoggerFactory>();
            loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

            var freshBuildClient = new AzureDevOpsBuildServerClient(loggerFactory, allowList);

            var crossOriginRefusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                freshBuildClient.TryReadArtifactFileTextAsync(
                    "https://tfs-project.corp.internal/tfs/DefaultCollection",
                    "https://tfs-other.corp.internal/tfs/DefaultCollection/drop.zip",
                    "bundle.json"));
            StringAssert.Contains(crossOriginRefusal.Message, "configured endpoint origin");

            var missingOriginRefusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                freshBuildClient.TryReadArtifactFileTextAsync(
                    string.Empty,
                    "https://tfs-other.corp.internal/tfs/DefaultCollection/drop.zip",
                    "bundle.json"));
            StringAssert.Contains(missingOriginRefusal.Message, "configured endpoint origin");

            var missingOriginCloudRefusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                freshBuildClient.TryReadArtifactFileTextAsync(
                    string.Empty,
                    "https://dev.azure.com/myorg/proj/_apis/build/builds/1/artifacts?artifactName=drop&$format=zip",
                    "bundle.json"));
            StringAssert.Contains(missingOriginCloudRefusal.Message, "configured endpoint origin");

            var webClientMissingOrigin = CreateAzureDevOpsClient(
                allowList,
                _ => new RecordingHttpHandler(_ =>
                {
                    handlerCalled = true;
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }),
                serverUrl: string.Empty);

            var webClientRefusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                webClientMissingOrigin.TryReadArtifactFileTextAsync(
                    "https://tfs-other.corp.internal/tfs/DefaultCollection/drop.zip",
                    "bundle.json"));
            StringAssert.Contains(webClientRefusal.Message, "configured endpoint origin");
            Assert.IsFalse(handlerCalled, "Missing configured server origin must never fall back to trusting artifactDownloadUrl.");

            var projectsPds = Substitute.For<IProjectsPersistentSource>();
            projectsPds.GetProject("OnPremBundleProj").Returns(new ProjectApiModel
            {
                ProjectId = 9,
                ProjectName = "OnPremBundleProj",
                ArtefactsUrl = "https://tfs-project.corp.internal/tfs/DefaultCollection",
                ArtefactsSubPaths = "CollectionA",
                ArtefactsBuildRegex = ".*",
                SourceControlType = SourceControlType.AzureDevOps
            });
            var componentsPds = Substitute.For<IComponentsPersistentSource>();
            componentsPds.GetComponentByName("App").Returns(new ComponentApiModel
            {
                ComponentId = 10,
                ComponentName = "App",
                ScriptPath = "deploy.ps1",
                IsEnabled = true,
                Children = new List<ComponentApiModel>()
            });
            var envsPds = Substitute.For<IEnvironmentsPersistentSource>();
            envsPds.GetEnvironment("Dev", Arg.Any<ClaimsPrincipal>()).Returns(new EnvironmentApiModel { EnvironmentName = "Dev" });
            var requestsPds = Substitute.For<IRequestsPersistentSource>();

            var stubBuildClient = Substitute.For<IBuildServerClient>();
            stubBuildClient.ValidateBuildAsync(
                    "https://tfs-project.corp.internal/tfs/DefaultCollection",
                    "CollectionA",
                    ".*",
                    "CollectionA; BundleDef",
                    null,
                    "vstfs:///Build/Build/42",
                    false)
                .Returns(new BuildServerBuildInfo
                {
                    BuildId = 42,
                    BuildNumber = "Bundle_1.0",
                    BuildUri = "vstfs:///Build/Build/42",
                    DefinitionName = "BundleDef",
                    ProjectName = "CollectionA"
                });
            stubBuildClient.GetBuildArtifactDownloadUrlAsync(
                    "https://tfs-project.corp.internal/tfs/DefaultCollection",
                    "CollectionA",
                    ".*",
                    "CollectionA; BundleDef",
                    "vstfs:///Build/Build/42")
                .Returns("https://tfs-other.corp.internal/tfs/DefaultCollection/drop.zip");
            stubBuildClient.TryReadArtifactFileTextAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>())
                .Returns(call => freshBuildClient.TryReadArtifactFileTextAsync(
                    call.ArgAt<string>(0),
                    call.ArgAt<string>(1),
                    call.ArgAt<string>(2)));

            var clientFactory = Substitute.For<IBuildServerClientFactory>();
            clientFactory.Create(SourceControlType.AzureDevOps).Returns(stubBuildClient);

            var deployLibrary = new DeployLibrary(
                projectsPds,
                componentsPds,
                Substitute.For<IManageProjectsPersistentSource>(),
                envsPds,
                loggerFactory,
                requestsPds,
                Substitute.For<IClaimsPrincipalReader>(),
                Substitute.For<IDeploymentEventsPublisher>(),
                clientFactory,
                allowList);

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                deployLibrary.SubmitRequest(
                    "OnPremBundleProj",
                    "Dev",
                    "vstfs:///Build/Build/42",
                    "CollectionA; BundleDef",
                    new List<string> { "App" },
                    new List<RequestProperty>(),
                    new ClaimsPrincipal()));

            requestsPds.DidNotReceiveWithAnyArgs().SubmitRequest(default!);
        }

        [TestMethod]
        public void AzureDevOpsRestApiClientDisablesAutomaticRedirects()
        {
            var apiClient = AzureDevOpsApiClientFactory.Create("https://tfs.corp.internal/tfs/DefaultCollection");
            Assert.IsInstanceOfType<AzureDevOpsApiClient>(apiClient, "REST API client must use non-redirecting AzureDevOpsApiClient.");

            var allowList = BuildAllowList(artefactHosts: ["tfs.corp.internal"]);
            var requestedUrls = new List<string>();
            var client = CreateAzureDevOpsClient(
                allowList,
                _ => new RecordingHttpHandler(req =>
                {
                    requestedUrls.Add(req.RequestUri!.AbsoluteUri);
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri("https://attacker.internal/harvest-ntlm");
                    return redirect;
                }),
                serverUrl: "https://tfs.corp.internal/tfs/DefaultCollection");

            Assert.Throws<Exception>(() =>
                client.GetBuildDefinitionsForProjects(
                    "https://tfs.corp.internal/tfs/",
                    "DefaultCollection\\ProjA",
                    ""));

            Assert.AreEqual(1, requestedUrls.Count, "REST client must not follow 302 redirect to attacker host.");
        }

        [TestMethod]
        public async Task AzureDevOpsCloudArtifactDownloadStripsBearerTokenAcrossCrossOriginRedirect()
        {
            var seenRequests = new List<(Uri Uri, AuthenticationHeaderValue? Authorization)>();
            var zipBytes = BuildZipWithEntry("bundle.json", "{\"items\":[]}");

            var client = CreateAzureDevOpsClient(
                BuildAllowList(),
                _ => new RecordingHttpHandler(req =>
                {
                    seenRequests.Add((req.RequestUri!, req.Headers.Authorization));
                    if (req.RequestUri!.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
                    {
                        var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                        redirect.Headers.Location = new Uri("https://artprodsu6weu.artifacts.visualstudio.com/signed-archive");
                        return redirect;
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(zipBytes)
                    };
                }));

            var content = await client.TryReadArtifactFileTextAsync(
                "https://dev.azure.com/myorg/proj/_apis/build/builds/10/artifacts?artifactName=drop&$format=zip",
                "bundle.json");

            Assert.AreEqual("{\"items\":[]}", content);
            Assert.AreEqual(2, seenRequests.Count);
            Assert.AreEqual("Bearer", seenRequests[0].Authorization?.Scheme);
            Assert.AreEqual("entra-secret-token", seenRequests[0].Authorization?.Parameter);
            Assert.IsNull(seenRequests[1].Authorization, "Cross-origin redirect must not carry Authorization header.");
        }

        [TestMethod]
        public async Task CatalogProviderRefusesDisallowedUnderlyingManifestRepositoryAndClearsCredentials()
        {
            var catalog = Substitute.For<ITemplateCatalog>();
            catalog.GetAsync("storage", "1.0.0", Arg.Any<CancellationToken>()).Returns(
                new TerraformTemplateManifest(
                    Name: "storage",
                    Version: "1.0.0",
                    Source: new TerraformTemplateSource(
                        Kind: "git",
                        Locator: "https://attacker.example/evil-module.git",
                        Ref: "refs/tags/storage/v1.0.0",
                        SubPath: null,
                        CommitSha: "0123456789abcdef0123456789abcdef01234567"),
                    Parameters: Array.Empty<TerraformTemplateParameter>(),
                    Outputs: Array.Empty<TerraformTemplateOutput>(),
                    Description: "test",
                    Tags: Array.Empty<string>(),
                    Category: "storage",
                    RequiredProviders: new Dictionary<string, string>(),
                    RequiredTerraformVersion: ">= 1.5.0",
                    Owner: "platform",
                    Deprecated: false,
                    DeprecationReason: null));

            var logger = Substitute.For<IRunnerLogger>();
            logger.FileLogger.Returns(Substitute.For<ILogger>());
            var provider = new CatalogReferenceCodeSourceProvider(logger, catalog);

            var scriptGroup = new ScriptGroup
            {
                TerraformTemplateName = "storage",
                TerraformTemplateVersion = "1.0.0",
                TerraformGitPat = "pat-secret",
                AzureBearerToken = "entra-secret",
                AllowedTerraformSourceHosts = new List<string> { "github.com" }
            };

            var refusal = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                provider.ProvisionCodeAsync(scriptGroup, Directory.GetCurrentDirectory(), CancellationToken.None));

            StringAssert.Contains(refusal.Message, "attacker.example");
            Assert.AreEqual(string.Empty, scriptGroup.TerraformGitPat);
            Assert.AreEqual(string.Empty, scriptGroup.AzureBearerToken);
            Assert.IsTrue(string.IsNullOrEmpty(scriptGroup.TerraformGitRepoUrl));
        }

        [TestMethod]
        public void CatalogProviderStripsEntraTokenForPermittedNonAzureDevOpsHost()
        {
            var scriptGroup = new ScriptGroup
            {
                TerraformGitPat = "pat-secret",
                AzureBearerToken = "entra-secret",
                AllowedTerraformSourceHosts = new List<string> { "github.com" }
            };

            CatalogReferenceCodeSourceProvider.EnforceCatalogSourceHostConfinement(
                "https://github.com/org/terraform-catalog.git",
                scriptGroup);

            Assert.AreEqual("pat-secret", scriptGroup.TerraformGitPat);
            Assert.AreEqual(string.Empty, scriptGroup.AzureBearerToken);
        }

        [TestMethod]
        public void CatalogProviderRejectsSubdomainWhenOnlyApexHostIsAllowedAndClearsCredentials()
        {
            var scriptGroup = new ScriptGroup
            {
                TerraformGitPat = "pat-secret",
                AzureBearerToken = "entra-secret",
                AllowedTerraformSourceHosts = new List<string> { "github.com" }
            };

            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() =>
                CatalogReferenceCodeSourceProvider.EnforceCatalogSourceHostConfinement(
                    "https://sub.github.com/org/terraform-catalog.git",
                    scriptGroup));

            StringAssert.Contains(refusal.Message, "sub.github.com");
            Assert.AreEqual(string.Empty, scriptGroup.TerraformGitPat);
            Assert.AreEqual(string.Empty, scriptGroup.AzureBearerToken);
        }

        [TestMethod]
        [DataRow(@"C:\catalog\modules\storage")]
        [DataRow("/var/dorc/catalog/storage")]
        [DataRow("file:///C:/catalog/modules/storage")]
        public void CatalogProviderAllowsLocalRepositoryPathAndStripsCredentialsWhenAllowListConfigured(string localLocator)
        {
            var scriptGroup = new ScriptGroup
            {
                TerraformGitPat = "pat-secret",
                AzureBearerToken = "entra-secret",
                AllowedTerraformSourceHosts = new List<string> { "github.com", "dev.azure.com" }
            };

            CatalogReferenceCodeSourceProvider.EnforceCatalogSourceHostConfinement(localLocator, scriptGroup);

            Assert.AreEqual(string.Empty, scriptGroup.TerraformGitPat);
            Assert.AreEqual(string.Empty, scriptGroup.AzureBearerToken);
        }

        [TestMethod]
        [DataRow(@"\\?\UNC\buildserver\drops\1.0", "buildserver")]
        [DataRow(@"//?/UNC/buildserver/drops/1.0", "buildserver")]
        [DataRow(@"\\.\UNC\buildserver\drops\1.0", "buildserver")]
        [DataRow(@"\??\UNC\buildserver\drops\1.0", "buildserver")]
        [DataRow(@"file://\\?\UNC\buildserver\drops\1.0", "buildserver")]
        public void SourceHostExtractsRemoteAuthorityFromExtendedAndDeviceUncPaths(string path, string expectedHost)
        {
            Assert.IsTrue(SourceHost.IsRemoteOrUnc(path), path);
            Assert.IsTrue(SourceHost.TryExtractRemoteHost(path, out var host), path);
            Assert.AreEqual(expectedHost, host, path);
            Assert.AreEqual(expectedHost, SourceHost.Of(path), path);
        }

        [TestMethod]
        [DataRow(@"\\attacker.com@SSL@443\DavWWWRoot\drop")]
        [DataRow(@"\\attacker.com@80\share\drop")]
        [DataRow(@"\\attacker.com@SSL\share\drop")]
        [DataRow(@"\\?\UNC\attacker.com@SSL@443\DavWWWRoot\drop")]
        [DataRow("file://attacker.com@SSL@443/DavWWWRoot/drop")]
        public void SourceHostAndPathConfinementFailClosedOnWebDavUncAuthorities(string webDavPath)
        {
            Assert.IsTrue(SourceHost.IsRemoteOrUnc(webDavPath), webDavPath);
            Assert.IsNull(SourceHost.Of(webDavPath), webDavPath);
            Assert.IsFalse(SourceHost.TryExtractRemoteHost(webDavPath, out _), webDavPath);
            Assert.IsFalse(PathConfinement.IsWithinAny(webDavPath, new[] { @"\\attacker.com\DavWWWRoot" }), webDavPath);
        }

        [TestMethod]
        [DataRow(@"C:\drops\1.0")]
        [DataRow("/var/drops/1.0")]
        [DataRow("file:///C:/drops/1.0")]
        public void SourceHostRecognizesLocalDiskPathsAsNonRemote(string localPath)
        {
            Assert.IsFalse(SourceHost.IsRemoteOrUnc(localPath), localPath);
            Assert.IsFalse(SourceHost.TryExtractRemoteHost(localPath, out _), localPath);
        }

        [TestMethod]
        [DataRow(@"\\attacker.corp\tf\module")]
        [DataRow(@"\\?\UNC\attacker.corp\tf\module")]
        [DataRow(@"\\allowed.corp@SSL@443\tf\module")]
        [DataRow(@"\\sub.allowed.corp\tf\module")]
        public async Task SharedFolderCodeSourceProviderRefusesDisallowedOrWebDavRemoteShareBeforeNetworkTouch(string remotePath)
        {
            var logger = Substitute.For<IRunnerLogger>();
            logger.FileLogger.Returns(Substitute.For<ILogger>());
            var provider = new SharedFolderCodeSourceProvider(logger);

            var unconfiguredGroup = new ScriptGroup { ScriptsLocation = remotePath };
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                provider.ProvisionCodeAsync(unconfiguredGroup, Directory.GetCurrentDirectory(), CancellationToken.None));

            var configuredGroup = new ScriptGroup
            {
                ScriptsLocation = remotePath,
                AllowedTerraformSourceHosts = new List<string> { "allowed.corp" }
            };
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                provider.ProvisionCodeAsync(configuredGroup, Directory.GetCurrentDirectory(), CancellationToken.None));
        }

        [TestMethod]
        public async Task GitHubActionsBuildServerClientRefusesUnconfiguredOrDisallowedHostBeforeSendingRequests()
        {
            var requestedUrls = new List<(Uri Uri, AuthenticationHeaderValue? Authorization)>();
            var zipBytes = BuildZipWithEntry("bundle.json", "{\"components\":[]}");
            var handler = new RecordingHttpHandler(req =>
            {
                requestedUrls.Add((req.RequestUri!, req.Headers.Authorization));
                if (req.RequestUri!.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri("https://pipelines.actions.githubusercontent.com/signed/drop.zip");
                    return redirect;
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(zipBytes)
                };
            });

            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory.CreateClient("GitHubActions").Returns(_ => new HttpClient(handler, disposeHandler: false));
            var hostValidator = new GitHubHostValidator(new ConfigurationBuilder().Build());
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppSettings:GitHubToken"] = "ghp_secret"
                })
                .Build();

            var unconfiguredClient = new GitHubActionsBuildServerClient(
                Substitute.For<ILogger<GitHubActionsBuildServerClient>>(),
                configuration,
                httpClientFactory,
                hostValidator,
                BuildAllowList());
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                unconfiguredClient.GetBuildsAsync("https://api.github.com/repos/myorg/myrepo", "build.yml", ".*", "CI", false));
            Assert.AreEqual(0, requestedUrls.Count);

            var disallowedClient = new GitHubActionsBuildServerClient(
                Substitute.For<ILogger<GitHubActionsBuildServerClient>>(),
                configuration,
                httpClientFactory,
                hostValidator,
                BuildAllowList(artefactHosts: ["ghe.corp.internal"]));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                disallowedClient.TryReadArtifactFileTextAsync(
                    "https://api.github.com/repos/myorg/myrepo",
                    "https://api.github.com/repos/myorg/myrepo/actions/artifacts/9/zip",
                    "bundle.json"));
            Assert.AreEqual(0, requestedUrls.Count);

            var permittedClient = new GitHubActionsBuildServerClient(
                Substitute.For<ILogger<GitHubActionsBuildServerClient>>(),
                configuration,
                httpClientFactory,
                hostValidator,
                BuildAllowList(artefactHosts: ["api.github.com"]));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                permittedClient.TryReadArtifactFileTextAsync(
                    string.Empty,
                    "https://api.github.com/repos/myorg/myrepo/actions/artifacts/9/zip",
                    "bundle.json"));
            Assert.AreEqual(0, requestedUrls.Count);

            var text = await permittedClient.TryReadArtifactFileTextAsync(
                "https://api.github.com/repos/myorg/myrepo",
                "https://api.github.com/repos/myorg/myrepo/actions/artifacts/9/zip",
                "bundle.json");

            Assert.AreEqual("{\"components\":[]}", text);
            Assert.AreEqual(2, requestedUrls.Count);
            Assert.AreEqual("ghp_secret", requestedUrls[0].Authorization?.Parameter);
            Assert.IsNull(requestedUrls[1].Authorization, "Redirect to Actions CDN must not carry GitHub Bearer token.");
        }

        [TestMethod]
        public void DeployLibraryRefusesNonBundleRemoteFileShareDropWhenAllowListUnconfiguredOrDisallowed()
        {
            var projectsPds = Substitute.For<IProjectsPersistentSource>();
            projectsPds.GetProject("ShareProj").Returns(new ProjectApiModel
            {
                ProjectId = 1,
                ProjectName = "ShareProj",
                ArtefactsUrl = @"\\attacker.corp\drops\ShareProj",
                SourceControlType = SourceControlType.FileShare
            });
            var componentsPds = Substitute.For<IComponentsPersistentSource>();
            componentsPds.GetComponentByName("App").Returns(
                new ComponentApiModel
                {
                    ComponentId = 10,
                    ComponentName = "App",
                    ScriptPath = "deploy.ps1",
                    IsEnabled = true,
                    Children = new List<ComponentApiModel>()
                });
            var envsPds = Substitute.For<IEnvironmentsPersistentSource>();
            envsPds.GetEnvironment("Dev", Arg.Any<ClaimsPrincipal>()).Returns(new EnvironmentApiModel { EnvironmentName = "Dev" });
            var requestsPds = Substitute.For<IRequestsPersistentSource>();

            var sut = new DeployLibrary(
                projectsPds,
                componentsPds,
                Substitute.For<IManageProjectsPersistentSource>(),
                envsPds,
                Substitute.For<ILoggerFactory>(),
                requestsPds,
                Substitute.For<IClaimsPrincipalReader>(),
                Substitute.For<IDeploymentEventsPublisher>(),
                Substitute.For<IBuildServerClientFactory>(),
                BuildAllowList());

            Assert.ThrowsExactly<InvalidOperationException>(() =>
                sut.SubmitRequest(
                    "ShareProj",
                    "Dev",
                    @"file://attacker.corp/drops/ShareProj/1.0",
                    string.Empty,
                    new List<string> { "App" },
                    new List<RequestProperty>(),
                    new ClaimsPrincipal()));

            requestsPds.DidNotReceiveWithAnyArgs().SubmitRequest(default!);
        }

        [TestMethod]
        public async Task GitHubArtifactProviderStripsBearerTokenAcrossCrossOriginArchiveRedirect()
        {
            var seenRequests = new List<(Uri Uri, AuthenticationHeaderValue? Authorization)>();
            var zipBytes = BuildZipWithEntry("main.tf", "terraform {}");
            var handler = new RecordingHttpHandler(req =>
            {
                seenRequests.Add((req.RequestUri!, req.Headers.Authorization));
                if (req.RequestUri!.AbsolutePath.EndsWith("/artifacts", StringComparison.Ordinal))
                {
                    const string json = """
                        {
                          "total_count": 1,
                          "artifacts": [
                            {
                              "id": 77,
                              "name": "drop",
                              "archive_download_url": "https://api.github.com/repos/myorg/myrepo/actions/artifacts/77/zip"
                            }
                          ]
                        }
                        """;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                }

                if (req.RequestUri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri("https://pipelines.actions.githubusercontent.com/signed/drop.zip");
                    return redirect;
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(zipBytes)
                };
            });

            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory.CreateClient("GitHubActions").Returns(_ => new HttpClient(handler, disposeHandler: false));

            var logger = Substitute.For<IRunnerLogger>();
            logger.FileLogger.Returns(Substitute.For<ILogger>());

            var workingDir = Path.Join(Directory.GetCurrentDirectory(), "gh-redirect-test-" + Guid.NewGuid().ToString("N"));
            var stagingDir = Path.Join(workingDir, "_staging");
            Directory.CreateDirectory(workingDir);
            var provider = new GitHubArtifactCodeSourceProvider(logger, httpClientFactory, stagingDir);
            try
            {
                var scriptGroup = new ScriptGroup
                {
                    GitHubOwner = "myorg",
                    GitHubRepo = "myrepo",
                    GitHubRunId = "555",
                    GitHubToken = "ghp_secret_token",
                    GitHubApiBaseUrl = "https://api.github.com",
                    AllowedArtefactHosts = new List<string> { "github.com" }
                };

                await provider.ProvisionCodeAsync(scriptGroup, workingDir, CancellationToken.None);

                Assert.IsTrue(File.Exists(Path.Join(workingDir, "main.tf")));
                Assert.AreEqual(3, seenRequests.Count);
                Assert.AreEqual("ghp_secret_token", seenRequests[0].Authorization?.Parameter);
                Assert.AreEqual("ghp_secret_token", seenRequests[1].Authorization?.Parameter);
                Assert.IsNull(seenRequests[2].Authorization, "Redirect to blob/CDN must not receive GitHub Bearer token.");
            }
            finally
            {
                if (Directory.Exists(workingDir))
                {
                    Directory.Delete(workingDir, recursive: true);
                }
            }
        }

        [TestMethod]
        public async Task AzureArtifactProviderRefusesDisallowedHostsAndStripsBearerTokenAcrossCrossOriginRedirect()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                AzureArtifactCodeSourceProvider.ValidateArtifactDownloadUrl("http://dev.azure.com/org/drop.zip"));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                AzureArtifactCodeSourceProvider.ValidateArtifactDownloadUrl("https://dev.azure.com.attacker.net/org/drop.zip"));

            var seenRequests = new List<(Uri Uri, AuthenticationHeaderValue? Authorization)>();
            var zipBytes = BuildZipWithEntry("main.tf", "terraform {}");
            var handler = new RecordingHttpHandler(req =>
            {
                seenRequests.Add((req.RequestUri!, req.Headers.Authorization));
                if (req.RequestUri!.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri("https://vsblobprod.blob.core.windows.net/signed/drop.zip");
                    return redirect;
                }

                var ok = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(zipBytes)
                };
                ok.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                return ok;
            });

            var logger = Substitute.For<IRunnerLogger>();
            logger.FileLogger.Returns(Substitute.For<ILogger>());

            var workingDir = Path.Join(Directory.GetCurrentDirectory(), "az-redirect-test-" + Guid.NewGuid().ToString("N"));
            var stagingDir = Path.Join(workingDir, "_staging");
            Directory.CreateDirectory(workingDir);
            var provider = new AzureArtifactCodeSourceProvider(logger, handler, stagingDir);
            try
            {
                var downloaded = await provider.DownloadFromUrlAsync(
                    "https://dev.azure.com/myorg/proj/_apis/build/builds/1/artifacts?artifactName=drop&$format=zip",
                    workingDir,
                    "entra-bearer-secret",
                    new List<string> { "dev.azure.com" },
                    CancellationToken.None);

                Assert.IsTrue(downloaded);
                Assert.IsTrue(File.Exists(Path.Join(workingDir, "main.tf")));
                Assert.AreEqual(2, seenRequests.Count);
                Assert.AreEqual("entra-bearer-secret", seenRequests[0].Authorization?.Parameter);
                Assert.IsNull(seenRequests[1].Authorization, "Redirect to Azure Blob storage must not receive Entra Bearer token.");
            }
            finally
            {
                if (Directory.Exists(workingDir))
                {
                    Directory.Delete(workingDir, recursive: true);
                }
            }
        }

        [TestMethod]
        [DataRow(@"\\?\C:\Terraform\module", @"C:\Terraform\module")]
        [DataRow(@"\\?\d:\Shares\Sub\file.ps1", @"d:\Shares\Sub\file.ps1")]
        [DataRow(@"//?/C:/Terraform/module", @"C:\Terraform\module")]
        public void SourceHostExtendedLocalDrivePathIsClassifiedAsLocalAndNormalized(
            string extendedPath,
            string expectedNormalized)
        {
            Assert.IsFalse(SourceHost.IsRemoteOrUnc(extendedPath));
            Assert.IsNull(SourceHost.Of(extendedPath));
            Assert.IsFalse(SourceHost.TryExtractRemoteHost(extendedPath, out _));
            Assert.AreEqual(expectedNormalized, SourceHost.NormalizeExtendedUnc(extendedPath));
        }

        [TestMethod]
        public void PathConfinementExtendedLocalDrivePathConfinesLocalAndRejectsUncAndDeviceNamespaces()
        {
            Assert.AreEqual("server", SourceHost.Of(@"\\?\UNC\server\share\sub"));
            Assert.IsTrue(SourceHost.IsRemoteOrUnc(@"\\?\UNC\server\share\sub"));

            foreach (var ambiguousDevicePath in new[]
            {
                @"\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}\Terraform\main.tf",
                @"\\.\PhysicalDrive0",
                @"\\.\C:\Terraform\main.tf"
            })
            {
                Assert.IsTrue(SourceHost.IsRemoteOrUnc(ambiguousDevicePath), ambiguousDevicePath);
                Assert.IsNull(SourceHost.Of(ambiguousDevicePath), ambiguousDevicePath);
                Assert.IsFalse(SourceHost.TryExtractRemoteHost(ambiguousDevicePath, out _), ambiguousDevicePath);
                Assert.IsFalse(PathConfinement.IsWithinAny(ambiguousDevicePath, [@"C:\Terraform"]), ambiguousDevicePath);
            }

            if (OperatingSystem.IsWindows())
            {
                Assert.IsTrue(PathConfinement.IsWithinAny(
                    @"\\?\C:\Shares\Sub\file.ps1",
                    [@"C:\Shares"]));
                Assert.IsTrue(PathConfinement.IsWithinAny(
                    @"C:\Shares\Sub\file.ps1",
                    [@"\\?\C:\Shares"]));
                Assert.IsFalse(PathConfinement.IsWithinAny(
                    @"\\?\C:\Shares\..\Other\file.ps1",
                    [@"C:\Shares"]));
            }
        }

        [TestMethod]
        public async Task SharedFolderProviderExtendedLocalDrivePathProvisionsWithoutRemoteHostAllowList()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var scratchRoot = Path.Join(
                Directory.GetCurrentDirectory(),
                "tf-ext-drive-test-" + Guid.NewGuid().ToString("N"));
            var sourceDir = Path.Join(scratchRoot, "src");
            var workDir = Path.Join(scratchRoot, "work");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(workDir);

            try
            {
                await File.WriteAllTextAsync(Path.Join(sourceDir, "main.tf"), "terraform {}");

                var logger = Substitute.For<IRunnerLogger>();
                logger.FileLogger.Returns(Substitute.For<ILogger>());
                var provider = new SharedFolderCodeSourceProvider(logger);
                var scriptGroup = new ScriptGroup
                {
                    ScriptsLocation = @"\\?\" + Path.GetFullPath(sourceDir),
                    AllowedTerraformSourceHosts = new List<string>()
                };

                await provider.ProvisionCodeAsync(scriptGroup, workDir, CancellationToken.None);
                Assert.IsTrue(File.Exists(Path.Join(workDir, "main.tf")));
            }
            finally
            {
                if (Directory.Exists(scratchRoot))
                {
                    Directory.Delete(scratchRoot, recursive: true);
                }
            }
        }

        [TestMethod]
        [DataRow("https://api.github.com:8443/repos/myorg/myrepo/actions/artifacts/77/zip")]
        [DataRow("https://github.com/repos/myorg/myrepo/actions/artifacts/77/zip")]
        public async Task GitHubArtifactProviderArchiveDownloadUrlOnDifferentPortOrPermittedHostRejectedWithoutSendingAuth(
            string mismatchedDownloadUrl)
        {
            var seenRequests = new List<(Uri Uri, AuthenticationHeaderValue? Authorization)>();
            var handler = new RecordingHttpHandler(req =>
            {
                seenRequests.Add((req.RequestUri!, req.Headers.Authorization));
                if (req.RequestUri!.AbsolutePath.EndsWith("/artifacts", StringComparison.Ordinal))
                {
                    var json = $$"""
                        {
                          "total_count": 1,
                          "artifacts": [
                            {
                              "id": 77,
                              "name": "drop",
                              "archive_download_url": "{{mismatchedDownloadUrl}}"
                            }
                          ]
                        }
                        """;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(BuildZipWithEntry("main.tf", "terraform {}"))
                };
            });

            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory.CreateClient("GitHubActions").Returns(_ => new HttpClient(handler, disposeHandler: false));

            var logger = Substitute.For<IRunnerLogger>();
            logger.FileLogger.Returns(Substitute.For<ILogger>());

            var workingDir = Path.Join(
                Directory.GetCurrentDirectory(),
                "gh-origin-test-" + Guid.NewGuid().ToString("N"));
            var stagingDir = Path.Join(workingDir, "_staging");
            Directory.CreateDirectory(workingDir);

            try
            {
                var provider = new GitHubArtifactCodeSourceProvider(logger, httpClientFactory, stagingDir);
                var scriptGroup = new ScriptGroup
                {
                    GitHubOwner = "myorg",
                    GitHubRepo = "myrepo",
                    GitHubRunId = "42",
                    GitHubToken = "ghs_estate_secret_token",
                    GitHubApiBaseUrl = "https://api.github.com",
                    AllowedArtefactHosts = new List<string> { "api.github.com", "github.com" }
                };

                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                    provider.ProvisionCodeAsync(scriptGroup, workingDir, CancellationToken.None));

                Assert.HasCount(1, seenRequests);
                Assert.AreEqual("api.github.com", seenRequests[0].Uri.DnsSafeHost);
                Assert.AreEqual(443, seenRequests[0].Uri.Port);
            }
            finally
            {
                if (Directory.Exists(workingDir))
                {
                    Directory.Delete(workingDir, recursive: true);
                }
            }
        }

        [TestMethod]
        [DataRow("https://ghe-a.corp.internal:8443/api/v3/repos/myorg/myrepo/actions/workflows?per_page=100&page=2")]
        [DataRow("https://ghe-b.corp.internal/api/v3/repos/myorg/myrepo/actions/workflows?per_page=100&page=2")]
        public async Task GitHubBuildClientPaginationNextLinkChangingPortOrPermittedHostThrowsWithoutSendingToken(
            string crossOriginNextUrl)
        {
            var seenRequests = new List<(Uri Uri, AuthenticationHeaderValue? Authorization)>();
            var handler = new RecordingHttpHandler(req =>
            {
                seenRequests.Add((req.RequestUri!, req.Headers.Authorization));
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"total_count\":1,\"workflows\":[{\"id\":1,\"name\":\"Other\",\"path\":\".github/workflows/other.yml\"}]}",
                        Encoding.UTF8,
                        "application/json")
                };
                response.Headers.TryAddWithoutValidation("Link", $"<{crossOriginNextUrl}>; rel=\"next\"");
                return response;
            });

            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory.CreateClient("GitHubActions").Returns(_ => new HttpClient(handler, disposeHandler: false));
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AppSettings:GitHubToken"] = "ghs_estate_secret_token",
                    ["AppSettings:AllowedGitHubEnterpriseHosts:0"] = "ghe-a.corp.internal",
                    ["AppSettings:AllowedGitHubEnterpriseHosts:1"] = "ghe-b.corp.internal"
                })
                .Build();
            var hostValidator = new GitHubHostValidator(configuration);
            var allowList = BuildAllowList(artefactHosts: ["ghe-a.corp.internal", "ghe-b.corp.internal"]);
            var client = new GitHubActionsBuildServerClient(
                Substitute.For<ILogger<GitHubActionsBuildServerClient>>(),
                configuration,
                httpClientFactory,
                hostValidator,
                allowList);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                client.GetBuildsAsync(
                    "https://ghe-a.corp.internal/repos/myorg/myrepo",
                    "ci.yml",
                    ".*",
                    "CI",
                    false));

            Assert.HasCount(1, seenRequests);
            Assert.AreEqual("ghe-a.corp.internal", seenRequests[0].Uri.DnsSafeHost);
            Assert.AreEqual(443, seenRequests[0].Uri.Port);
        }

        [TestMethod]
        [DataRow("../../evil.txt", "evil.txt")]
        [DataRow(@"..\..\evil.txt", "evil.txt")]
        public async Task AzureDevOpsApiClientContentDispositionTraversalFilenameWritesStrictlyInsideTempFolder(
            string rawDispositionFilename,
            string expectedLeafName)
        {
            var scratchRoot = Path.Join(
                Directory.GetCurrentDirectory(),
                "ado-api-client-test-" + Guid.NewGuid().ToString("N"));
            var tempDir = Path.Join(scratchRoot, "temp");
            Directory.CreateDirectory(tempDir);

            try
            {
                var expectedPayload = Encoding.UTF8.GetBytes("confined-artifact-bytes");
                var config = new Org.OpenAPITools.Client.Configuration
                {
                    BasePath = "https://dev.azure.com/myorg",
                    TempFolderPath = tempDir
                };
                var codec = new AzureDevOpsApiClient.AzureDevOpsJsonCodec(
                    new Newtonsoft.Json.JsonSerializerSettings(),
                    config);
                var restResponse = new RestSharp.RestResponse(new RestSharp.RestRequest())
                {
                    RawBytes = expectedPayload,
                    Headers =
                    [
                        new RestSharp.HeaderParameter(
                            "Content-Disposition",
                            $"attachment; filename=\"{rawDispositionFilename}\"")
                    ]
                };

                await using var stream = codec.Deserialize<Stream>(restResponse);
                var fileStream = stream as FileStream;
                Assert.IsNotNull(fileStream);

                var expectedFile = Path.Join(Path.GetFullPath(tempDir), expectedLeafName);
                Assert.AreEqual(expectedFile, Path.GetFullPath(fileStream.Name));
                Assert.IsFalse(File.Exists(Path.Join(scratchRoot, expectedLeafName)));

                using var ms = new MemoryStream();
                await fileStream.CopyToAsync(ms);
                CollectionAssert.AreEqual(expectedPayload, ms.ToArray());
            }
            finally
            {
                if (Directory.Exists(scratchRoot))
                {
                    Directory.Delete(scratchRoot, recursive: true);
                }
            }
        }

        private static AzureDevOpsServerWebClient CreateAzureDevOpsClient(
            ISourceHostAllowList allowList,
            Func<bool, HttpMessageHandler>? handlerFactory = null,
            string serverUrl = "https://dev.azure.com/myorg")
        {
            var tokenGenerator = Substitute.For<IAuthTokenGenerator>();
            tokenGenerator.GetToken().Returns("entra-secret-token");
            return new AzureDevOpsServerWebClient(
                serverUrl,
                "dev.azure.com",
                tokenGenerator,
                Substitute.For<ILogger>(),
                allowList,
                handlerFactory);
        }

        private static SourceHostAllowList BuildAllowList(string[]? artefactHosts = null)
        {
            var entries = new Dictionary<string, string?>();
            if (artefactHosts != null)
            {
                for (var i = 0; i < artefactHosts.Length; i++)
                {
                    entries[$"{SourceHostAllowList.ArtefactHostsSetting}:{i}"] = artefactHosts[i];
                }
            }

            return new SourceHostAllowList(new ConfigurationBuilder().AddInMemoryCollection(entries).Build());
        }

        private static byte[] BuildZipWithEntry(string entryName, string content)
        {
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }

            return memory.ToArray();
        }

        private sealed class RecordingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                Task.FromResult(responder(request));
        }
    }
}

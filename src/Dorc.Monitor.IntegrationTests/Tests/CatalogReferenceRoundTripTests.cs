using Dorc.ApiModel;
using Dorc.Monitor.Tests.Init;
using Dorc.PersistentData.Contexts;
using Dorc.PersistentData.Model;
using Dorc.PersistentData.Sources.Interfaces;
using Dorc.Runner.Logger;
using Dorc.Terraform.Catalog;
using Dorc.TerraformRunner.CodeSources;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Reflection;
using System.Security.Cryptography;

namespace Dorc.Monitor.IntegrationTests.Tests
{
    /// <summary>
    /// round-trip of TerraformTemplateName / TerraformTemplateVersion through
    /// the SSDT schema, EF entity, EF type configuration, and persistent-source
    /// mappings (write + read projections); plus the per-source-type regression for
    /// non-Catalog component kinds.
    ///
    /// Uses the real database configured in appsettings.test.json. Each test seeds
    /// its own Project + Component(s), captures their ids, and tears them down by id
    /// in a finally block transactional rollback would not enclose
    /// ManageProjectsPersistentSource's per-call DbContext usings.
    /// </summary>
    [TestClass]
    public class CatalogReferenceRoundTripTests : MonitorServiceTestBase
    {
        // Properties on ComponentApiModel that are intentionally not asserted in
        // the per-source-type regression test. Each entry is excluded because
        // the persistence layer (or the post-save mutation in CreateComponent)
        // re-shapes the value independently of the round-trip the test verifies.
        // The list is captured as a constant so any future additions to the
        // model force a deliberate test edit rather than silently being skipped.
        // Note: SPEC §4 Test 3 mentions excluding `SecurityObject` audit/identity
        // fields (`ObjectId`, `Updated*`, `Created*`); ComponentApiModel does
        // not extend SecurityObject and exposes none of those properties, so
        // they are absent here.
        private static readonly HashSet<string> RegressionExclusions = new(StringComparer.Ordinal)
        {
            // Mutated post-save by CreateComponent.
            nameof(ComponentApiModel.ComponentId),
            // Shaped by Parent reference loading01.
            nameof(ComponentApiModel.ParentId),
            // Initialised to `new List<>()` by ComponentsPersistentSource.MapToComponentApiModel
            // regardless of input; the test does not exercise child loading.
            nameof(ComponentApiModel.Children),
            // Projections normalise null → "" via `component.Script?.Path ?? ""`;
            // round-trip is null on the way in, "" on the way out for non-script
            // source types.
            nameof(ComponentApiModel.ScriptPath),
            // CreateComponent passes PSVersion through `ToSafePsVersionString()`
            // which maps null/"" → "v7" before persistence. Round-trip is not
            // an identity for this property by design.
            nameof(ComponentApiModel.PSVersion),
        };

        // Test 1 Catalog create round-trip via both getter paths.
        [TestMethod]
        public void CreateComponent_CatalogMode_RoundTripsViaBothGetterPaths()
        {
            const string sentinelTemplateName = "sql-database";
            const string sentinelTemplateVersion = "1.0.0";

            var manageSource = provider.GetRequiredService<IManageProjectsPersistentSource>();
            var componentsSource = provider.GetRequiredService<IComponentsPersistentSource>();

            var ext = RandomNumberGenerator.GetHexString(8);
            var projectName = "s001-proj-" + ext;
            var componentName = "s001-cat-" + ext;
            int projectId = 0;
            int componentId = 0;

            try
            {
                projectId = SeedProject(projectName);

                var apiComponent = new ComponentApiModel
                {
                    ComponentId = 0,
                    ComponentName = componentName,
                    IsEnabled = true,
                    StopOnFailure = false,
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = sentinelTemplateName,
                    TerraformTemplateVersion = sentinelTemplateVersion,
                };

                manageSource.CreateComponent(apiComponent, projectId, parentId: null, username: "s001-test");
                componentId = apiComponent.ComponentId ?? 0;
                Assert.AreNotEqual(0, componentId, "CreateComponent must mutate ComponentId on save.");

                // Reload via ComponentsPersistentSource.GetComponentByName exercises Site 3.
                var reloadedByName = componentsSource.GetComponentByName(componentName);
                Assert.IsNotNull(reloadedByName, "Component should be findable by name after save.");
                Assert.AreEqual(sentinelTemplateName, reloadedByName.TerraformTemplateName,
                    "ComponentsPersistentSource read projection (Site 3) must populate TerraformTemplateName.");
                Assert.AreEqual(sentinelTemplateVersion, reloadedByName.TerraformTemplateVersion,
                    "ComponentsPersistentSource read projection (Site 3) must populate TerraformTemplateVersion.");
                Assert.AreEqual(TerraformSourceType.Catalog, reloadedByName.TerraformSourceType);

                // Reload via direct EF query through the context exercises the entity round-trip
                // independently of the API-shape projections, proving the columns themselves persist.
                var contextFactory = provider.GetRequiredService<IDeploymentContextFactory>();
                using (var context = contextFactory.GetContext())
                {
                    var entity = context.Components.SingleOrDefault(c => c.Id == componentId);
                    Assert.IsNotNull(entity, "Component entity should be retrievable by id.");
                    Assert.AreEqual(sentinelTemplateName, entity.TerraformTemplateName,
                        "EF entity must round-trip TerraformTemplateName.");
                    Assert.AreEqual(sentinelTemplateVersion, entity.TerraformTemplateVersion,
                        "EF entity must round-trip TerraformTemplateVersion.");
                }
            }
            finally
            {
                if (componentId != 0) DeleteComponent(componentId);
                if (projectId != 0) DeleteProject(projectId);
            }
        }

        // Test 2 Catalog update round-trip.
        [TestMethod]
        public void UpdateComponent_CatalogMode_RoundTripsViaBothGetterPaths()
        {
            var manageSource = provider.GetRequiredService<IManageProjectsPersistentSource>();
            var componentsSource = provider.GetRequiredService<IComponentsPersistentSource>();

            var ext = RandomNumberGenerator.GetHexString(8);
            var projectName = "s001-proj-" + ext;
            var componentName = "s001-cat-upd-" + ext;
            int projectId = 0;
            int componentId = 0;

            try
            {
                projectId = SeedProject(projectName);

                // Create with sentinel A.
                var apiComponent = new ComponentApiModel
                {
                    ComponentId = 0,
                    ComponentName = componentName,
                    IsEnabled = true,
                    StopOnFailure = false,
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = "storage-account",
                    TerraformTemplateVersion = "1.0.0",
                };
                manageSource.CreateComponent(apiComponent, projectId, parentId: null, username: "s001-test");
                componentId = apiComponent.ComponentId ?? 0;
                Assert.AreNotEqual(0, componentId);

                // Update to sentinel B.
                apiComponent.TerraformTemplateName = "sql-database";
                apiComponent.TerraformTemplateVersion = "1.2.0";
                manageSource.UpdateComponent(apiComponent, projectId, parentId: null, username: "s001-test");

                // Reload via Site 3.
                var reloadedByName = componentsSource.GetComponentByName(componentName);
                Assert.IsNotNull(reloadedByName);
                Assert.AreEqual("sql-database", reloadedByName.TerraformTemplateName);
                Assert.AreEqual("1.2.0", reloadedByName.TerraformTemplateVersion);

                // Reload via EF entity directly to confirm the persistence layer (not just the projection) updated.
                var contextFactory = provider.GetRequiredService<IDeploymentContextFactory>();
                using (var context = contextFactory.GetContext())
                {
                    var entity = context.Components.Single(c => c.Id == componentId);
                    Assert.AreEqual("sql-database", entity.TerraformTemplateName);
                    Assert.AreEqual("1.2.0", entity.TerraformTemplateVersion);
                }
            }
            finally
            {
                if (componentId != 0) DeleteComponent(componentId);
                if (projectId != 0) DeleteProject(projectId);
            }
        }

        // Test 3 Per-source-type regression: PowerShell + each non-Catalog Terraform
        // source type. Reflection-based field equality across the round-trip, with the
        // documented exclusion set; both new catalog-reference columns asserted null
        // after reload.
        [TestMethod]
        [DataRow(ComponentType.PowerShell, TerraformSourceType.SharedFolder, "ps-script.ps1", null, null,
            DisplayName = "PowerShell")]
        [DataRow(ComponentType.Terraform, TerraformSourceType.Git, null, "main", "infra/sample",
            DisplayName = "Terraform/Git")]
        [DataRow(ComponentType.Terraform, TerraformSourceType.SharedFolder, "//share/tf", null, null,
            DisplayName = "Terraform/SharedFolder")]
        [DataRow(ComponentType.Terraform, TerraformSourceType.AzureArtifact, null, null, "infra/sample",
            DisplayName = "Terraform/AzureArtifact")]
        public void NonCatalogSourceTypes_RoundTripIdentically_NewColumnsNullAfterReload(
            ComponentType componentType,
            TerraformSourceType sourceType,
            string? scriptPath,
            string? gitBranch,
            string? subPath)
        {
            var manageSource = provider.GetRequiredService<IManageProjectsPersistentSource>();
            var componentsSource = provider.GetRequiredService<IComponentsPersistentSource>();

            var ext = RandomNumberGenerator.GetHexString(8);
            var projectName = "s001-proj-" + ext;
            var componentName = $"s001-{componentType}-{sourceType}-{ext}";
            int projectId = 0;
            int componentId = 0;

            try
            {
                projectId = SeedProject(projectName);

                var input = new ComponentApiModel
                {
                    ComponentId = 0,
                    ComponentName = componentName,
                    IsEnabled = true,
                    StopOnFailure = false,
                    ComponentType = componentType,
                    TerraformSourceType = sourceType,
                    ScriptPath = scriptPath,
                    TerraformGitBranch = gitBranch,
                    TerraformSubPath = subPath,
                    // The persistent source's non-script projection branch hard-codes
                    // NonProdOnly=true (line ~749 in ManageProjectsPersistentSource),
                    // and PSVersion is sourced from the Script entity. Set inputs to
                    // align with the read path so reflection-based equality passes.
                    NonProdOnly = !(componentType == ComponentType.PowerShell ||
                                    sourceType == TerraformSourceType.SharedFolder),
                    PSVersion = (componentType == ComponentType.PowerShell ||
                                 sourceType == TerraformSourceType.SharedFolder)
                        ? string.Empty
                        : null,
                };

                manageSource.CreateComponent(input, projectId, parentId: null, username: "s001-test");
                componentId = input.ComponentId ?? 0;
                Assert.AreNotEqual(0, componentId);

                var reloaded = componentsSource.GetComponentByName(componentName);
                Assert.IsNotNull(reloaded, $"{componentType}/{sourceType} should reload by name.");

                // negative assertion: the new catalog-reference columns are null
                // (not the empty string) for every non-Catalog source type after reload.
                Assert.IsNull(reloaded.TerraformTemplateName,
                    $"{sourceType}: TerraformTemplateName must be null after reload (was '{reloaded.TerraformTemplateName}').");
                Assert.IsNull(reloaded.TerraformTemplateVersion,
                    $"{sourceType}: TerraformTemplateVersion must be null after reload.");

                // Reflection-based equality across the rest of the public properties.
                AssertPublicPropertiesEqual(input, reloaded, sourceType);
            }
            finally
            {
                if (componentId != 0) DeleteComponent(componentId);
                if (projectId != 0) DeleteProject(projectId);
            }
        }

        // Test 4 A Catalog-mode Component reloaded from the DB carries enough
        // information for CatalogReferenceCodeSourceProvider's required-field guards
        // to pass. We verify this by routing the reloaded component's fields into a
        // ScriptGroup and invoking ProvisionCodeAsync against a stubbed ITemplateCatalog
        // whose returned manifest has Source.Kind="not-git". The resolver's required-
        // field guards (lines 32-41 of CatalogReferenceCodeSourceProvider) are checked
        // first; if they passed, control reaches the kind-check (lines 64-69) which
        // throws InvalidOperationException with "unsupported source kind". Catching
        // that exception proves the round-tripped fields satisfied the guards.
        [TestMethod]
        public async Task CatalogReferenceProvider_RequiredFieldGuardsPass_AfterRoundTrip()
        {
            var manageSource = provider.GetRequiredService<IManageProjectsPersistentSource>();
            var componentsSource = provider.GetRequiredService<IComponentsPersistentSource>();

            var ext = RandomNumberGenerator.GetHexString(8);
            var projectName = "s001-proj-" + ext;
            var componentName = "s001-cat-disp-" + ext;
            const string sentinelTemplateName = "sql-database";
            const string sentinelTemplateVersion = "1.0.0";
            int projectId = 0;
            int componentId = 0;

            try
            {
                projectId = SeedProject(projectName);
                var apiComponent = new ComponentApiModel
                {
                    ComponentId = 0,
                    ComponentName = componentName,
                    IsEnabled = true,
                    StopOnFailure = false,
                    ComponentType = ComponentType.Terraform,
                    TerraformSourceType = TerraformSourceType.Catalog,
                    TerraformTemplateName = sentinelTemplateName,
                    TerraformTemplateVersion = sentinelTemplateVersion,
                };
                manageSource.CreateComponent(apiComponent, projectId, parentId: null, username: "s001-test");
                componentId = apiComponent.ComponentId ?? 0;

                var reloaded = componentsSource.GetComponentByName(componentName);
                Assert.IsNotNull(reloaded);

                // Build a ScriptGroup mirroring the catalog branch of TerraformSourceConfigurator:
                // it copies TerraformSourceType and the catalog reference fields onto the
                // script group; per-component dispatch then invokes the resolver.
                var scriptGroup = new ScriptGroup
                {
                    TerraformSourceType = reloaded.TerraformSourceType,
                    TerraformTemplateName = reloaded.TerraformTemplateName,
                    TerraformTemplateVersion = reloaded.TerraformTemplateVersion,
                };

                // Stub catalog returns a manifest with an unsupported source kind so the
                // resolver throws AFTER the required-fields guards.
                var stubCatalog = Substitute.For<ITemplateCatalog>();
                var manifest = new TerraformTemplateManifest(
                    Name: sentinelTemplateName,
                    Version: sentinelTemplateVersion,
                    Source: new TerraformTemplateSource(Kind: "not-git", Locator: "n/a", Ref: "n/a"),
                    Parameters: Array.Empty<TerraformTemplateParameter>(),
                    Outputs: Array.Empty<TerraformTemplateOutput>(),
                    Description: null,
                    Tags: Array.Empty<string>(),
                    Category: null,
                    RequiredProviders: new Dictionary<string, string>(),
                    RequiredTerraformVersion: ">= 1.5.0",
                    Owner: null,
                    Deprecated: false,
                    DeprecationReason: null);
                stubCatalog
                    .GetAsync(sentinelTemplateName, sentinelTemplateVersion, Arg.Any<CancellationToken>())
                    .Returns(manifest);

                var stubLogger = Substitute.For<IRunnerLogger>();
                var sut = new CatalogReferenceCodeSourceProvider(stubLogger, stubCatalog);

                var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
                {
                    await sut.ProvisionCodeAsync(scriptGroup, workingDir: Path.GetTempPath(), CancellationToken.None);
                });

                StringAssert.Contains(ex.Message, "unsupported source kind",
                    "Resolver should reach the kind-check (proving required-field guards passed); " +
                    "if the guards had failed, the exception message would mention TerraformTemplateName/Version.");
            }
            finally
            {
                if (componentId != 0) DeleteComponent(componentId);
                if (projectId != 0) DeleteProject(projectId);
            }
        }

        // ----- Helpers -----

        private int SeedProject(string projectName)
        {
            var contextFactory = provider.GetRequiredService<IDeploymentContextFactory>();
            using (var context = contextFactory.GetContext())
            {
                var project = new Project
                {
                    Name = projectName,
                    ObjectId = Guid.NewGuid(),
                };
                context.Projects.Add(project);
                context.SaveChanges();
                return project.Id;
            }
        }

        private void DeleteComponent(int componentId)
        {
            var contextFactory = provider.GetRequiredService<IDeploymentContextFactory>();
            using (var context = contextFactory.GetContext())
            {
                var component = context.Components
                    .Where(c => c.Id == componentId).SingleOrDefault();
                if (component is null) return;
                if (component.Script is not null)
                    context.Set<Script>().Remove(component.Script);
                context.Components.Remove(component);
                context.SaveChanges();
            }
        }

        private void DeleteProject(int projectId)
        {
            var contextFactory = provider.GetRequiredService<IDeploymentContextFactory>();
            using (var context = contextFactory.GetContext())
            {
                var project = context.Projects.SingleOrDefault(p => p.Id == projectId);
                if (project is null) return;
                context.Projects.Remove(project);
                context.SaveChanges();
            }
        }

        private static void AssertPublicPropertiesEqual(
            ComponentApiModel expected,
            ComponentApiModel actual,
            TerraformSourceType sourceType)
        {
            var properties = typeof(ComponentApiModel)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => !RegressionExclusions.Contains(p.Name));
            foreach (var p in properties)
            {
                var expectedValue = p.GetValue(expected);
                var actualValue = p.GetValue(actual);
                Assert.AreEqual(expectedValue, actualValue,
                    $"{sourceType}: property '{p.Name}' differs across save→reload (expected '{expectedValue}', got '{actualValue}').");
            }
        }
    }
}

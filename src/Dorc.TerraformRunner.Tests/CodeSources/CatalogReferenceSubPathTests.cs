using Dorc.Terraform.Catalog;
using Dorc.TerraformRunner.CodeSources;

namespace Dorc.TerraformRunner.Tests.CodeSources
{
    /// <summary>
    /// Tests for CatalogReferenceCodeSourceProvider.ResolveSubPath.
    /// The helper is a pure function of the manifest implementing the lower
    /// two precedence levels (manifest > convention); the third level (an
    /// explicit scriptGroup.TerraformSubPath overriding everything) is
    /// enforced at the call site, where it already lived.
    /// </summary>
    [TestClass]
    public class CatalogReferenceSubPathTests
    {
        // Minimal manifest factory; every test only varies Source.SubPath and Name.
        private static TerraformTemplateManifest BuildManifest(string name, string? subPath)
            => new(
                Name: name,
                Version: "1.0.0",
                Source: new TerraformTemplateSource(
                    Kind: "git",
                    Locator: "https://example.com/repo.git",
                    Ref: "v1.0.0",
                    SubPath: subPath),
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

        // Test 3 manifest's non-empty SubPath wins.
        [TestMethod]
        public void ResolveSubPath_ReturnsManifestValue_WhenSourceSubPathNonEmpty()
        {
            var manifest = BuildManifest(name: "any-name", subPath: "modules/custom");

            var resolved = CatalogReferenceCodeSourceProvider.ResolveSubPath(manifest);

            Assert.AreEqual("modules/custom", resolved);
        }

        // Test 4 null manifest SubPath falls back to convention.
        [TestMethod]
        public void ResolveSubPath_ReturnsConvention_WhenSourceSubPathNull()
        {
            var manifest = BuildManifest(name: "sql-database", subPath: null);

            var resolved = CatalogReferenceCodeSourceProvider.ResolveSubPath(manifest);

            Assert.AreEqual("stock-modules/sql-database", resolved);
        }

        // Test 4b empty-string manifest SubPath falls back to convention,
        // pinning the IsNullOrEmpty contract against future drift to is-not-null.
        [TestMethod]
        public void ResolveSubPath_ReturnsConvention_WhenSourceSubPathEmpty()
        {
            var manifest = BuildManifest(name: "sql-database", subPath: "");

            var resolved = CatalogReferenceCodeSourceProvider.ResolveSubPath(manifest);

            Assert.AreEqual("stock-modules/sql-database", resolved);
        }
    }
}

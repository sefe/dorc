using Dorc.Terraform.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dorc.Terraform.Catalog.Tests
{
    /// <summary>
    /// Tests for GitTemplateCatalog covering (additive Sensitive + SubPath
    /// fields) and (load-time validation rules: parameter-name charset,
    /// parameter-type allow-list, vnet-shape regression guard).
    ///
    /// The YAML payloads are embedded as verbatim C# string literals so a future
    /// model change cannot silently mutate the test fixture.
    /// </summary>
    [TestClass]
    public class ManifestLoaderTests
    {
        private string _tempDir = string.Empty;

        [TestInitialize]
        public void CreateTempDir()
        {
            _tempDir = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void DeleteTempDir()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Cleanup is best-effort; surface but do not fail the test.
                Console.WriteLine($"Temp-dir cleanup failed for '{_tempDir}': {ex.Message}");
            }
        }

        // Test 1 loader honours new YAML fields.
        [TestMethod]
        public async Task LoadsNewFields_WhenYamlPopulatesThem()
        {
            var yaml = @"name: testmod
version: 1.0.0
source:
  kind: git
  locator: https://example.com/repo.git
  ref: v1.0.0
  sub_path: modules/custom
parameters:
  - name: alpha
    type: String
    required: true
    sensitive: true
  - name: beta
    type: String
    required: false
outputs: []
description: test manifest
tags: []
required_providers: {}
required_terraform_version: "">= 1.5.0""
deprecated: false
";
            File.WriteAllText(Path.Join(_tempDir, "testmod-1.0.0.yaml"), yaml);

            var catalog = new GitTemplateCatalog(_tempDir, NullLogger<GitTemplateCatalog>.Instance);
            var manifest = await catalog.GetAsync("testmod", "1.0.0");

            Assert.IsNotNull(manifest, "Manifest should load.");
            Assert.AreEqual("modules/custom", manifest.Source.SubPath,
                "Source.SubPath should reflect the YAML value.");

            var alpha = manifest.Parameters.Single(p => p.Name == "alpha");
            Assert.IsTrue(alpha.Sensitive,
                "Parameter 'alpha' has sensitive: true in YAML; should round-trip as Sensitive=true.");

            var beta = manifest.Parameters.Single(p => p.Name == "beta");
            Assert.IsFalse(beta.Sensitive,
                "Parameter 'beta' has no sensitive key in YAML; should default to false.");
        }

        //  exactly two shipped manifests load (vnet removed by)
        // and zero warnings are emitted (the surviving manifests pass the new rules cleanly).
        [TestMethod]
        public async Task LoadsAllShippedManifests_WithoutError()
        {
            var manifestsDir = FindShippedManifestsDir();
            Assert.IsNotNull(manifestsDir,
                "Could not find the repo's stock-modules-manifests/ directory by walking up from AppContext.BaseDirectory.");

            var recordingLogger = new RecordingLogger<GitTemplateCatalog>();
            var catalog = new GitTemplateCatalog(manifestsDir, recordingLogger);
            var manifests = await catalog.ListAsync();

            Assert.AreEqual(2, manifests.Count,
                $"Expected exactly two shipped manifests in {manifestsDir}; got {manifests.Count}.");

            var names = manifests.Select(m => m.Name).ToList();
            CollectionAssert.Contains(names, "sql-database",
                "sql-database manifest should load.");
            CollectionAssert.Contains(names, "storage-account",
                "storage-account manifest should load.");
            CollectionAssert.DoesNotContain(names, "vnet",
                "vnet was removed in alongside the load-time rules; reinstated in v2 with complex-type runner support.");

            Assert.AreEqual(0, recordingLogger.Warnings.Count,
                $"API startup is clean surviving manifests must load without warnings. Got: {string.Join(" | ", recordingLogger.Warnings)}");
        }

        // Walks up from AppContext.BaseDirectory looking for a sibling-or-ancestor
        // directory named "stock-modules-manifests" containing at least one .yaml.
        private static string? FindShippedManifestsDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Join(dir.FullName, "stock-modules-manifests");
                if (Directory.Exists(candidate) &&
                    Directory.EnumerateFiles(candidate, "*.yaml", SearchOption.TopDirectoryOnly).Any())
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
            return null;
        }

        // Test 2 loader handles missing new fields (back-compat).
        [TestMethod]
        public async Task LoadsDefaults_WhenYamlOmitsNewFields()
        {
            var yaml = @"name: legacymod
version: 0.9.0
source:
  kind: git
  locator: https://example.com/repo.git
  ref: v0.9.0
parameters:
  - name: alpha
    type: String
    required: true
outputs: []
description: legacy manifest with no new fields
tags: []
required_providers: {}
required_terraform_version: "">= 1.5.0""
deprecated: false
";
            File.WriteAllText(Path.Join(_tempDir, "legacymod-0.9.0.yaml"), yaml);

            var catalog = new GitTemplateCatalog(_tempDir, NullLogger<GitTemplateCatalog>.Instance);
            var manifest = await catalog.GetAsync("legacymod", "0.9.0");

            Assert.IsNotNull(manifest);
            Assert.IsNull(manifest.Source.SubPath,
                "Source.SubPath default is null when the YAML omits the key.");
            Assert.IsFalse(manifest.Parameters.Single().Sensitive,
                "Parameter.Sensitive default is false when the YAML omits the key.");
        }

        // Test 1 reject manifest with hyphenated parameter name.
        [TestMethod]
        public async Task RejectsManifest_WhenParameterNameContainsHyphen()
        {
            var yaml = @"name: badname
version: 1.0.0
source:
  kind: git
  locator: https://example.com/repo.git
  ref: v1.0.0
parameters:
  - name: bad-name
    type: String
    required: true
outputs: []
description: bad parameter name
tags: []
required_providers: {}
required_terraform_version: "">= 1.5.0""
deprecated: false
";
            var path = Path.Join(_tempDir, "badname-1.0.0.yaml");
            File.WriteAllText(path, yaml);

            var recordingLogger = new RecordingLogger<GitTemplateCatalog>();
            var catalog = new GitTemplateCatalog(_tempDir, recordingLogger);
            var manifests = await catalog.ListAsync();

            Assert.AreEqual(0, manifests.Count, "Manifest with bad parameter name must be rejected.");
            Assert.AreEqual(1, recordingLogger.Warnings.Count, "Exactly one WARNING should be emitted.");
            StringAssert.Contains(recordingLogger.Warnings[0], "bad-name",
                "WARNING should name the offending parameter.");
            StringAssert.Contains(recordingLogger.Warnings[0], path,
                "WARNING should include the file path.");
        }

        // Test 2 reject manifest with List-typed parameter.
        [TestMethod]
        public async Task RejectsManifest_WhenParameterTypeIsList()
        {
            var yaml = BuildSingleParamYaml("listmod", "1.0.0", "subnets", "List");
            var path = Path.Join(_tempDir, "listmod-1.0.0.yaml");
            File.WriteAllText(path, yaml);

            var recordingLogger = new RecordingLogger<GitTemplateCatalog>();
            var catalog = new GitTemplateCatalog(_tempDir, recordingLogger);
            var manifests = await catalog.ListAsync();

            Assert.AreEqual(0, manifests.Count);
            Assert.AreEqual(1, recordingLogger.Warnings.Count);
            StringAssert.Contains(recordingLogger.Warnings[0], "List");
            StringAssert.Contains(recordingLogger.Warnings[0], "subnets");
        }

        // Test 3 reject manifest with Map-typed parameter.
        [TestMethod]
        public async Task RejectsManifest_WhenParameterTypeIsMap()
        {
            var yaml = BuildSingleParamYaml("mapmod", "1.0.0", "tags", "Map");
            var path = Path.Join(_tempDir, "mapmod-1.0.0.yaml");
            File.WriteAllText(path, yaml);

            var recordingLogger = new RecordingLogger<GitTemplateCatalog>();
            var catalog = new GitTemplateCatalog(_tempDir, recordingLogger);
            var manifests = await catalog.ListAsync();

            Assert.AreEqual(0, manifests.Count);
            Assert.AreEqual(1, recordingLogger.Warnings.Count);
            StringAssert.Contains(recordingLogger.Warnings[0], "Map");
            StringAssert.Contains(recordingLogger.Warnings[0], "tags");
        }

        // Test 4 reject manifest with Object-typed parameter.
        [TestMethod]
        public async Task RejectsManifest_WhenParameterTypeIsObject()
        {
            var yaml = BuildSingleParamYaml("objmod", "1.0.0", "config", "Object");
            var path = Path.Join(_tempDir, "objmod-1.0.0.yaml");
            File.WriteAllText(path, yaml);

            var recordingLogger = new RecordingLogger<GitTemplateCatalog>();
            var catalog = new GitTemplateCatalog(_tempDir, recordingLogger);
            var manifests = await catalog.ListAsync();

            Assert.AreEqual(0, manifests.Count);
            Assert.AreEqual(1, recordingLogger.Warnings.Count);
            StringAssert.Contains(recordingLogger.Warnings[0], "Object");
            StringAssert.Contains(recordingLogger.Warnings[0], "config");
        }

        // Test 5 mixed dir: one valid + one rejected.
        [TestMethod]
        public async Task RejectsBadButLoadsValidManifest_InMixedDir()
        {
            var validYaml = BuildSingleParamYaml("validmod", "1.0.0", "alpha", "String");
            var invalidYaml = BuildSingleParamYaml("badmod", "1.0.0", "config", "Object");
            File.WriteAllText(Path.Join(_tempDir, "validmod-1.0.0.yaml"), validYaml);
            var badPath = Path.Join(_tempDir, "badmod-1.0.0.yaml");
            File.WriteAllText(badPath, invalidYaml);

            var recordingLogger = new RecordingLogger<GitTemplateCatalog>();
            var catalog = new GitTemplateCatalog(_tempDir, recordingLogger);
            var manifests = await catalog.ListAsync();

            Assert.AreEqual(1, manifests.Count, "Only the valid manifest should load.");
            Assert.AreEqual("validmod", manifests[0].Name);
            Assert.AreEqual(1, recordingLogger.Warnings.Count);
            StringAssert.Contains(recordingLogger.Warnings[0], "badmod-1.0.0.yaml");
        }

        // Test 6 vnet-shape regression guard. Verbatim copy of the
        // deleted vnet-1.0.0.yaml content (captured before §2d's git rm).
        // Pins the rule against any future re-add; lives in temp dir, so the
        // immutability gate (which scans stock-modules-manifests/*.yaml
        // PR diffs) does not interact.
        [TestMethod]
        public async Task RejectsVnetShapedManifest_RegressionGuard()
        {
            var yaml = @"name: vnet
version: 1.0.0
source:
  kind: git
  locator: https://github.com/sefe/dorc.git
  ref: stock-modules/vnet/v1.0.0
parameters:
  - name: resource_group_name
    type: String
    required: true
    description: Existing resource group name (1-90 characters).
  - name: location
    type: String
    required: true
    description: Azure region; must match the resource group.
  - name: vnet_name
    type: String
    required: true
    description: Name of the virtual network.
  - name: address_space
    type: List
    required: true
    description: One or more CIDR blocks for the vnet address space.
  - name: subnets
    type: List
    required: false
    description: Subnets to create inside the vnet (list of object{name, address_prefix}).
  - name: tags
    type: Map
    required: false
    description: Tags applied to every resource.
outputs: []
description: vnet-shaped regression guard
tags: []
required_providers: {}
required_terraform_version: "">= 1.5.0""
deprecated: false
";
            File.WriteAllText(Path.Join(_tempDir, "vnet-1.0.0.yaml"), yaml);

            var recordingLogger = new RecordingLogger<GitTemplateCatalog>();
            var catalog = new GitTemplateCatalog(_tempDir, recordingLogger);
            var manifests = await catalog.ListAsync();

            Assert.AreEqual(0, manifests.Count, "vnet-shape manifest must be rejected.");
            Assert.AreEqual(1, recordingLogger.Warnings.Count);
            StringAssert.Contains(recordingLogger.Warnings[0], "List",
                "First-violation-wins finds 'address_space: List' before 'subnets: List' in declaration order.");
        }

        // ----- Latest-version resolution (GetAsync(name) overload) -----

        // Guards the numeric per-component ordering: lexical string ordering
        // would pick 1.9.0 over 1.10.0.
        [TestMethod]
        public async Task GetAsyncLatest_OrdersVersionsNumericallyPerComponent()
        {
            File.WriteAllText(Path.Join(_tempDir, "testmod-1.9.0.yaml"),
                BuildSingleParamYaml("testmod", "1.9.0", "alpha", "String"));
            File.WriteAllText(Path.Join(_tempDir, "testmod-1.10.0.yaml"),
                BuildSingleParamYaml("testmod", "1.10.0", "alpha", "String"));

            var catalog = new GitTemplateCatalog(_tempDir, NullLogger<GitTemplateCatalog>.Instance);
            var latest = await catalog.GetAsync("testmod");

            Assert.IsNotNull(latest, "GetAsync(name) should resolve the template.");
            Assert.AreEqual("1.10.0", latest.Version,
                "1.10.0 is numerically newer than 1.9.0; lexical ordering would wrongly pick 1.9.0.");
        }

        [TestMethod]
        public async Task GetAsyncLatest_VPrefixedVersionParsesAndWins()
        {
            File.WriteAllText(Path.Join(_tempDir, "testmod-1.9.0.yaml"),
                BuildSingleParamYaml("testmod", "1.9.0", "alpha", "String"));
            File.WriteAllText(Path.Join(_tempDir, "testmod-v2.0.0.yaml"),
                BuildSingleParamYaml("testmod", "v2.0.0", "alpha", "String"));

            var catalog = new GitTemplateCatalog(_tempDir, NullLogger<GitTemplateCatalog>.Instance);
            var latest = await catalog.GetAsync("testmod");

            Assert.IsNotNull(latest, "GetAsync(name) should resolve the template.");
            Assert.AreEqual("v2.0.0", latest.Version,
                "A 'v'-prefixed version must parse numerically (2.0.0) and beat 1.9.0.");
        }

        // ----- Helpers -----

        // Generates a minimal valid YAML with one parameter of the supplied
        // (paramName, paramType). Used by Tests 2-5 to keep boilerplate focused.
        private static string BuildSingleParamYaml(string name, string version, string paramName, string paramType)
        {
            return $@"name: {name}
version: {version}
source:
  kind: git
  locator: https://example.com/repo.git
  ref: v{version}
parameters:
  - name: {paramName}
    type: {paramType}
    required: true
outputs: []
description: test fixture for {name}
tags: []
required_providers: {{}}
required_terraform_version: "">= 1.5.0""
deprecated: false
";
        }
    }

    /// <summary>
    /// Test ILogger that captures rendered messages (formatter applied) keyed by
    /// LogLevel. Avoids NSubstitute's brittle ReceivedCalls verification on
    /// extension methods like LogWarning.
    /// </summary>
    internal sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<string> Warnings =>
            _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoOpDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }

        // Returned by BeginScope so any `using (var s = logger.BeginScope(...))`
        // call site sees a non-null disposable (more robust than `return null`).
        private sealed class NoOpDisposable : IDisposable
        {
            public static readonly NoOpDisposable Instance = new();
            public void Dispose() { }
        }
    }
}

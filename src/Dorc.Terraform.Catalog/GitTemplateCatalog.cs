using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Dorc.Terraform.Catalog
{
    // Reads catalog manifests from a directory containing one YAML file per
    // (name, version): manifests/<name>-<version>.yaml. The directory may be
    // a checked-out Git working tree (the eventual production wiring) or a
    // local filesystem path (the test wiring); this class is unaware of that
    // distinction. A future GitFetchingTemplateCatalog can wrap this with
    // git-clone/refresh semantics.
    public sealed class GitTemplateCatalog : ITemplateCatalog
    {
        private readonly string manifestsDirectory;

        public GitTemplateCatalog(string manifestsDirectory)
        {
            if (string.IsNullOrEmpty(manifestsDirectory)) throw new ArgumentException("manifestsDirectory required", nameof(manifestsDirectory));
            this.manifestsDirectory = manifestsDirectory;
        }

        public Task<IReadOnlyList<TerraformTemplateManifest>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(manifestsDirectory))
            {
                return Task.FromResult<IReadOnlyList<TerraformTemplateManifest>>(Array.Empty<TerraformTemplateManifest>());
            }

            var deserializer = BuildDeserializer();
            var manifests = new List<TerraformTemplateManifest>();
            foreach (var file in Directory.EnumerateFiles(manifestsDirectory, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var yaml = File.ReadAllText(file);
                var manifest = deserializer.Deserialize<TerraformTemplateManifest>(yaml);
                if (manifest is not null) manifests.Add(manifest);
            }
            return Task.FromResult<IReadOnlyList<TerraformTemplateManifest>>(manifests);
        }

        public async Task<TerraformTemplateManifest?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            var all = await ListAsync(cancellationToken).ConfigureAwait(false);
            // Highest semver wins; semver compared lexically here is naive but
            // sufficient for stock-modules tags in the form vMAJOR.MINOR.PATCH.
            return all.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                      .OrderByDescending(m => m.Version, StringComparer.Ordinal)
                      .FirstOrDefault();
        }

        public async Task<TerraformTemplateManifest?> GetAsync(string name, string version, CancellationToken cancellationToken = default)
        {
            var all = await ListAsync(cancellationToken).ConfigureAwait(false);
            return all.FirstOrDefault(m =>
                string.Equals(m.Name, name, StringComparison.Ordinal) &&
                string.Equals(m.Version, version, StringComparison.Ordinal));
        }

        private static IDeserializer BuildDeserializer()
        {
            return new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
        }
    }
}

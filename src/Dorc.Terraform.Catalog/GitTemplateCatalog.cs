using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
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
    //
    // Implementation note: YamlDotNet's DefaultObjectFactory
    // requires parameterless ctors, but TerraformTemplateManifest et al. are
    // positional records (no parameterless ctor). The loader therefore
    // deserialises into a private mutable DTO graph and projects to the
    // immutable record at the boundary. Property names on the DTOs match the
    // record fields exactly so the existing UnderscoredNamingConvention
    // continues to map YAML keys (`required_terraform_version`,
    // `allowed_values`, `sub_path`, etc.) to the DTO setters without further
    // configuration.
    public sealed class GitTemplateCatalog : ITemplateCatalog
    {
        private readonly string manifestsDirectory;
        private readonly ILogger<GitTemplateCatalog> logger;

        // validation rules. Parameter names must match this charset so
        // they round-trip through the runner's tfvars-writer sanitiser without
        // silent mangling. Allowed parameter types are restricted to those the
        // tfvars writer maps faithfully; complex types are deferred
        // to v2 alongside runner support.
        private static readonly Regex ParameterNameRegex =
            new("^[a-zA-Z0-9_]+$", RegexOptions.Compiled);
        // Template name/version flow into the runner's git arguments and the
        // stock-modules/{name} sub-path fallback, and into DB columns
        // (name<=256, version<=64). Constrain both to a safe charset+length so
        // a manifest cannot smuggle path/argument metacharacters downstream.
        private static readonly Regex TemplateNameRegex =
            new(@"^[a-zA-Z0-9._-]{1,256}$", RegexOptions.Compiled);
        private static readonly Regex TemplateVersionRegex =
            new(@"^[a-zA-Z0-9._-]{1,64}$", RegexOptions.Compiled);
        private static readonly HashSet<TerraformParameterType> AllowedParameterTypes =
            new() { TerraformParameterType.String, TerraformParameterType.Number, TerraformParameterType.Bool };

        public GitTemplateCatalog(string manifestsDirectory, ILogger<GitTemplateCatalog> logger)
        {
            if (string.IsNullOrEmpty(manifestsDirectory)) throw new ArgumentException("manifestsDirectory required", nameof(manifestsDirectory));
            this.manifestsDirectory = manifestsDirectory;
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<IReadOnlyList<TerraformTemplateManifest>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(manifestsDirectory))
            {
                return Task.FromResult<IReadOnlyList<TerraformTemplateManifest>>(Array.Empty<TerraformTemplateManifest>());
            }

            var deserializer = BuildDeserializer();
            var manifests = new List<TerraformTemplateManifest>();
            var seenIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Enumerate in a stable (ordinal filename) order so resolution is
            // deterministic regardless of filesystem enumeration order.
            foreach (var file in Directory.EnumerateFiles(manifestsDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();

                ManifestDto? dto;
                try
                {
                    var yaml = File.ReadAllText(file);
                    dto = deserializer.Deserialize<ManifestDto>(yaml);
                }
                catch (Exception ex) when (ex is YamlDotNet.Core.YamlException || ex is IOException)
                {
                    // A single malformed manifest (bad YAML, or an
                    // unrecognized enum value like `type: list` that YamlDotNet
                    // rejects before the allow-list rule can skip-and-warn)
                    // must not abort the whole catalog - that would take down
                    // template listing, instantiation, and the runner's source
                    // resolution for EVERY template. Warn and skip, matching the
                    // validation-rule rejection path below.
                    logger.LogWarning(ex, "Catalog manifest at {File} could not be read/parsed and was skipped.", file);
                    continue;
                }

                var (manifest, rejectReason) = TryValidateAndProject(dto);
                if (rejectReason is not null)
                {
                    // Validation-rule rejection: emit a WARNING and skip.
                    logger.LogWarning("Catalog manifest at {File} rejected: {Reason}", file, rejectReason);
                    continue;
                }
                if (manifest is not null)
                {
                    // Reject a second file declaring the same (name, version):
                    // the per-(name,version) immutability CI gate only guards
                    // edits to an existing file, not a duplicate-identity file
                    // added elsewhere, which would otherwise make resolution
                    // ambiguous. Keep the first (ordinal filename order) and warn.
                    var identity = manifest.Name + "@" + manifest.Version;
                    if (!seenIdentities.Add(identity))
                    {
                        logger.LogWarning(
                            "Catalog manifest at {File} declares duplicate identity {Identity}; ignoring in favour of the first file seen.",
                            file, identity);
                        continue;
                    }
                    manifests.Add(manifest);
                }
                // (manifest == null && rejectReason == null) preserves the
                // silent-skip path for under-specified DTOs.
            }
            return Task.FromResult<IReadOnlyList<TerraformTemplateManifest>>(manifests);
        }

        // applies the new validation rules and projects to the record.
        // Returns one of three states (the fourth both non-null is unreachable):
        //   (manifest != null, reason == null) valid; add to result
        //   (null, reason != null)              rejected by a rule; logged + skipped
        //   (null, null)                        under-specified DTO; silent skip
        private static (TerraformTemplateManifest? manifest, string? rejectReason) TryValidateAndProject(ManifestDto? dto)
        {
            if (dto is null) return (null, null);
            if (string.IsNullOrEmpty(dto.Name)) return (null, null);
            if (string.IsNullOrEmpty(dto.Version)) return (null, null);
            if (dto.Source is null) return (null, null);

            if (!TemplateNameRegex.IsMatch(dto.Name))
                return (null, $"template name '{dto.Name}' is invalid (only [a-zA-Z0-9._-], up to 256 chars)");
            if (!TemplateVersionRegex.IsMatch(dto.Version))
                return (null, $"template version '{dto.Version}' is invalid (only [a-zA-Z0-9._-], up to 64 chars)");

            // Iterate parameters in YAML declaration order so the first violation
            // determines the WARNING message deterministically.
            foreach (var p in dto.Parameters ?? new List<ParameterDto>())
            {
                var name = p.Name ?? string.Empty;
                if (string.IsNullOrEmpty(name) || !ParameterNameRegex.IsMatch(name))
                {
                    return (null, $"parameter '{name}' has an invalid name (only [a-zA-Z0-9_] are accepted)");
                }
                if (!AllowedParameterTypes.Contains(p.Type))
                {
                    return (null, $"parameter '{name}' has unsupported type '{p.Type}' (only String/Number/Bool are accepted in v1)");
                }
            }

            return (ToManifest(dto), null);
        }

        public async Task<TerraformTemplateManifest?> GetAsync(string name, CancellationToken cancellationToken = default)
        {
            var all = await ListAsync(cancellationToken).ConfigureAwait(false);
            // Highest version wins, compared numerically per dotted component
            // (lexical ordering would pick 1.9.0 over 1.10.0). Versions that
            // do not parse sort below any parseable version and fall back to
            // ordinal comparison amongst themselves.
            return all.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                      .OrderByDescending(m => TryParseVersion(m.Version), NullsFirstVersionComparer.Instance)
                      .ThenByDescending(m => m.Version, StringComparer.Ordinal)
                      .FirstOrDefault();
        }

        private static Version? TryParseVersion(string version)
        {
            var v = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version[1..] : version;
            // System.Version needs at least major.minor.
            if (!v.Contains('.')) v += ".0";
            return Version.TryParse(v, out var parsed) ? parsed : null;
        }

        private sealed class NullsFirstVersionComparer : IComparer<Version?>
        {
            public static readonly NullsFirstVersionComparer Instance = new();
            public int Compare(Version? x, Version? y)
            {
                if (x is null && y is null) return 0;
                if (x is null) return -1;
                if (y is null) return 1;
                return x.CompareTo(y);
            }
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

        // ----- Mutable DTOs for YamlDotNet -----
        // Internal-by-design: not exposed outside this class. Defaults preserve
        // the existing record-shape semantics for YAMLs that omit a field.

        private sealed class ManifestDto
        {
            public string? Name { get; set; }
            public string? Version { get; set; }
            public SourceDto? Source { get; set; }
            public List<ParameterDto>? Parameters { get; set; }
            public List<OutputDto>? Outputs { get; set; }
            public string? Description { get; set; }
            public List<string>? Tags { get; set; }
            public string? Category { get; set; }
            public Dictionary<string, string>? RequiredProviders { get; set; }
            public string? RequiredTerraformVersion { get; set; }
            public string? Owner { get; set; }
            public bool Deprecated { get; set; }
            public string? DeprecationReason { get; set; }
        }

        private sealed class SourceDto
        {
            public string? Kind { get; set; }
            public string? Locator { get; set; }
            public string? Ref { get; set; }
            public string? SubPath { get; set; }
        }

        private sealed class ParameterDto
        {
            public string? Name { get; set; }
            public TerraformParameterType Type { get; set; }
            public bool Required { get; set; }
            public string? Description { get; set; }
            public string? Default { get; set; }
            public List<string>? AllowedValues { get; set; }
            public string? Pattern { get; set; }
            public decimal? Min { get; set; }
            public decimal? Max { get; set; }
            public bool Sensitive { get; set; }
        }

        private sealed class OutputDto
        {
            public string? Name { get; set; }
            public TerraformParameterType Type { get; set; }
            public string? Description { get; set; }
            public bool Sensitive { get; set; }
        }

        // ----- DTO → record projection -----

        private static TerraformTemplateManifest? ToManifest(ManifestDto? dto)
        {
            if (dto is null) return null;
            // Skip under-specified manifests: the three identity fields the
            // catalog's lookup keys against. Richer validation (parameter
            // shapes, source kind, etc.) is territory.
            if (string.IsNullOrEmpty(dto.Name)) return null;
            if (string.IsNullOrEmpty(dto.Version)) return null;
            if (dto.Source is null) return null;

            return new TerraformTemplateManifest(
                Name: dto.Name,
                Version: dto.Version,
                Source: ToSource(dto.Source),
                Parameters: (dto.Parameters ?? new()).Select(ToParameter).ToList(),
                Outputs: (dto.Outputs ?? new()).Select(ToOutput).ToList(),
                Description: dto.Description,
                Tags: dto.Tags ?? new List<string>(),
                Category: dto.Category,
                RequiredProviders: dto.RequiredProviders ?? new Dictionary<string, string>(),
                RequiredTerraformVersion: dto.RequiredTerraformVersion ?? string.Empty,
                Owner: dto.Owner,
                Deprecated: dto.Deprecated,
                DeprecationReason: dto.DeprecationReason);
        }

        private static TerraformTemplateSource ToSource(SourceDto? dto)
        {
            if (dto is null)
                return new TerraformTemplateSource(Kind: string.Empty, Locator: string.Empty, Ref: string.Empty);
            return new TerraformTemplateSource(
                Kind: dto.Kind ?? string.Empty,
                Locator: dto.Locator ?? string.Empty,
                Ref: dto.Ref ?? string.Empty,
                SubPath: dto.SubPath);
        }

        private static TerraformTemplateParameter ToParameter(ParameterDto dto)
        {
            return new TerraformTemplateParameter(
                Name: dto.Name ?? string.Empty,
                Type: dto.Type,
                Required: dto.Required,
                Description: dto.Description,
                Default: dto.Default,
                AllowedValues: dto.AllowedValues,
                Pattern: dto.Pattern,
                Min: dto.Min,
                Max: dto.Max,
                Sensitive: dto.Sensitive);
        }

        private static TerraformTemplateOutput ToOutput(OutputDto dto)
        {
            return new TerraformTemplateOutput(
                Name: dto.Name ?? string.Empty,
                Type: dto.Type,
                Description: dto.Description,
                Sensitive: dto.Sensitive);
        }
    }
}

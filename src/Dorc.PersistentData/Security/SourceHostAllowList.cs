using Dorc.ApiModel;
using Microsoft.Extensions.Configuration;

namespace Dorc.PersistentData.Security
{
    /// <summary>
    /// Decides whether a URL naming a source of deployable content points at a host the
    /// deployment is permitted to fetch from.
    ///
    /// Three fields carry such a URL, all of them settable at per-project modify rights and
    /// none of them inspected today beyond a scheme-prefix test:
    ///
    /// - a project's artefacts URL, which becomes the drop location scripts are read from;
    /// - a project's Terraform repository URL, which is cloned and executed;
    /// - a Terraform component's shared-folder location, which is the same thing by another
    ///   route (W-5a).
    ///
    /// Each becomes execution input, and two of them attract a credential on the way. The
    /// pattern is not new to this codebase - the GitHub API host has been checked against an
    /// allow-list for exactly this reason - it simply never reached these three.
    /// </summary>
    public interface ISourceHostAllowList
    {
        /// <summary>
        /// Whether a build artefact location may be fetched from. The value is the project's
        /// artefacts URL: one root, or several separated by semicolons.
        /// </summary>
        PolicyDecision CheckArtefactSource(string? url);

        /// <summary>
        /// Whether Terraform code may be provisioned from this location - a git repository
        /// URL or a shared folder.
        /// </summary>
        PolicyDecision CheckTerraformSource(string? url);

        bool IsArtefactSourceUnconfigured { get; }

        bool IsTerraformSourceUnconfigured { get; }

        /// <summary>
        /// True when neither list is configured, so no confinement is in force. Callers that
        /// want to report the gap can ask; the decision to leave it unenforced is deliberate
        /// and explained on <see cref="SourceHostAllowList"/>.
        /// </summary>
        bool IsUnconfigured { get; }
    }

    public class SourceHostAllowList : ISourceHostAllowList
    {
        public const string ArtefactHostsSetting = "AppSettings:AllowedArtefactHosts";
        public const string TerraformHostsSetting = "AppSettings:AllowedTerraformSourceHosts";

        private readonly IReadOnlyCollection<string> artefactHosts;
        private readonly IReadOnlyCollection<string> terraformHosts;

        /// <exception cref="InvalidOperationException">
        /// A list is present in configuration but is not a list of host names. Thrown at
        /// construction - which is process start-up - so that a mistyped setting is an error
        /// the operator sees rather than an allow-list that silently admits everything.
        /// </exception>
        public SourceHostAllowList(IConfiguration configuration)
        {
            artefactHosts = Read(configuration, ArtefactHostsSetting);
            terraformHosts = Read(configuration, TerraformHostsSetting);
        }

        public bool IsArtefactSourceUnconfigured => artefactHosts.Count == 0;

        public bool IsTerraformSourceUnconfigured => terraformHosts.Count == 0;

        public bool IsUnconfigured => artefactHosts.Count == 0 && terraformHosts.Count == 0;

        public PolicyDecision CheckArtefactSource(string? url)
        {
            // ArtefactsUrl uses the same semicolon-delimited root format consumed by
            // PathConfinement. Every non-empty root is independently executable input, so
            // permitting only the first would leave later roots as an allow-list bypass.
            var roots = (url ?? string.Empty).Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var root in roots)
            {
                var decision = Check(root, artefactHosts, ArtefactHostsSetting);

                if (!decision.Allowed)
                {
                    return decision;
                }
            }

            return PolicyDecision.Allow();
        }

        public PolicyDecision CheckTerraformSource(string? url) =>
            Check(url, terraformHosts, TerraformHostsSetting);

        private static PolicyDecision Check(string? url, IReadOnlyCollection<string> allowed, string setting)
        {
            if (allowed.Count == 0)
            {
                // Not configured, so not enforced. There is no safe built-in default for a host
                // list - every estate's hosts are its own - and an empty list is
                // indistinguishable from one an operator has not filled in yet. Enforcing
                // against nothing would reject every project edit in every deployment on the
                // day this ships, which is the flag day the sequencing rules exist to prevent.
                // The gap is reportable through IsUnconfigured rather than silent.
                return PolicyDecision.Allow();
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                // Nothing named, nothing fetched. Whether the field may be empty at all is a
                // separate question, already asked elsewhere.
                return PolicyDecision.Allow();
            }

            var host = SourceHost.Of(url);

            if (host == null)
            {
                return PolicyDecision.Refuse(
                    $"'{LogText.SingleLine(url.Trim())}' does not name a host that can be identified, so it"
                    + " cannot be checked against the permitted list.");
            }

            if (allowed.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                return PolicyDecision.Allow();
            }

            return PolicyDecision.Refuse(
                $"its host '{host}' is not permitted. Add it to '{setting}' if it should be.");
        }

        private static IReadOnlyCollection<string> Read(IConfiguration configuration, string setting)
        {
            var section = configuration.GetSection(setting);

            if (!section.Exists() || string.IsNullOrWhiteSpace(section.Value) && !section.GetChildren().Any())
            {
                // Absent, or present and blank. Either is "not configured".
                return Array.Empty<string>();
            }

            if (section.Value != null)
            {
                // A single value where a list was expected: "a.corp;b.corp" written as one
                // string rather than as a JSON array. The configuration binder returns null for
                // this, which would have read as "not configured" and admitted every host. A
                // list that is present but unreadable is an error, not an absence.
                throw new InvalidOperationException(
                    $"'{setting}' must be a list of host names, one per entry, but it is the single value"
                    + $" '{section.Value}'. Write it as a JSON array, or as indexed entries ({setting}:0, {setting}:1, ...).");
            }

            return section.GetChildren()
                .Select(child => child.Value)
                .Where(host => !string.IsNullOrWhiteSpace(host))
                .Select(host => host!.Trim())
                .ToArray();
        }
    }
}

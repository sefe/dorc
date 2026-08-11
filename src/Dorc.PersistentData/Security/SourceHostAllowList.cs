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
        /// Whether a build artefact location may be fetched from. On false,
        /// <paramref name="reason"/> explains, in terms a caller can show a user.
        /// </summary>
        bool IsArtefactSourceAllowed(string? url, out string reason);

        /// <summary>
        /// Whether Terraform code may be provisioned from this location - a git repository
        /// URL or a shared folder.
        /// </summary>
        bool IsTerraformSourceAllowed(string? url, out string reason);

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

        public SourceHostAllowList(IConfiguration configuration)
        {
            artefactHosts = Read(configuration, ArtefactHostsSetting);
            terraformHosts = Read(configuration, TerraformHostsSetting);
        }

        public bool IsUnconfigured => artefactHosts.Count == 0 && terraformHosts.Count == 0;

        public bool IsArtefactSourceAllowed(string? url, out string reason) =>
            IsAllowed(url, artefactHosts, ArtefactHostsSetting, out reason);

        public bool IsTerraformSourceAllowed(string? url, out string reason) =>
            IsAllowed(url, terraformHosts, TerraformHostsSetting, out reason);

        private static bool IsAllowed(
            string? url,
            IReadOnlyCollection<string> allowed,
            string setting,
            out string reason)
        {
            reason = string.Empty;

            if (allowed.Count == 0)
            {
                // Not configured, so not enforced. There is no safe built-in default for a host
                // list - every estate's hosts are its own - and an empty list is
                // indistinguishable from one an operator has not filled in yet. Enforcing
                // against nothing would reject every project edit in every deployment on the
                // day this ships, which is the flag day the sequencing rules exist to prevent.
                // The gap is reportable through IsUnconfigured rather than silent.
                return true;
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                // Nothing named, nothing fetched. Whether the field may be empty at all is a
                // separate question, already asked elsewhere.
                return true;
            }

            var host = HostOf(url);

            if (host == null)
            {
                reason = $"'{url}' does not name a host that can be identified, so it cannot be"
                    + " checked against the permitted list.";
                return false;
            }

            if (allowed.Contains(host, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            reason = $"its host '{host}' is not permitted. Add it to '{setting}' if it should be.";
            return false;
        }

        /// <summary>
        /// The host of a URL or UNC path, or null when none can be established.
        ///
        /// Parsed rather than matched. A substring test against the URL text - the shape this
        /// codebase uses elsewhere to decide whether a host is "the" host - is satisfied by
        /// any attacker-chosen host that merely contains the expected one, so
        /// https://build.corp.example.com.attacker.net passes a test for
        /// "build.corp.example.com". Only comparing the parsed authority avoids that, which is
        /// why the host is extracted here and compared whole.
        /// </summary>
        public static string? HostOf(string? urlOrUncPath)
        {
            if (string.IsNullOrWhiteSpace(urlOrUncPath))
            {
                return null;
            }

            var value = urlOrUncPath.Trim();

            // A UNC path is not a URI, and Uri.TryCreate on Linux does not treat \\host\share
            // as one either. Read the host directly so this answers the same way wherever it
            // runs.
            if (value.StartsWith(@"\\", StringComparison.Ordinal))
            {
                var segments = value[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
                return segments.Length > 0 ? segments[0] : null;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return null;
            }

            // file://host/share and file:///C:/local differ: the second names no host, and a
            // local path on the API host is not a source anything should be fetched from.
            return string.IsNullOrEmpty(uri.Host) ? null : uri.Host;
        }

        private static IReadOnlyCollection<string> Read(IConfiguration configuration, string setting)
        {
            var configured = configuration.GetSection(setting).Get<string[]>();

            if (configured == null)
            {
                return Array.Empty<string>();
            }

            return configured
                .Where(host => !string.IsNullOrWhiteSpace(host))
                .Select(host => host.Trim())
                .ToArray();
        }
    }
}

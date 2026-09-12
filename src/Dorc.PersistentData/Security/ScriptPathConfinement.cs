using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dorc.PersistentData.Security
{
    /// <summary>
    /// Decides whether a registered script path can only ever name a file beneath the
    /// configured script root.
    ///
    /// A component's script path is stored relative to that root and joined to it at dispatch
    /// with Path.Combine. Path.Combine does not join — given a rooted second argument it
    /// discards the first entirely, so a stored value of <c>\\elsewhere\share\payload.ps1</c>
    /// is not resolved beneath the root, it *replaces* it. The result is executed as the
    /// deployment account. That turns modify rights on a single project into arbitrary code
    /// execution, and bypasses the gated promotion pipeline that protects the script share.
    ///
    /// The rule is therefore stated over the stored value rather than over the joined result:
    /// a path that is relative and free of traversal cannot resolve outside the root, whatever
    /// the root turns out to be. That is exactly equivalent to canonicalising against the root
    /// and comparing, and it needs no knowledge of the root — so this validation can run at
    /// the write path, which has no reason to know where scripts live.
    /// </summary>
    public static class ScriptPathConfinement
    {
        private const string ScriptPathProperty = "ScriptPath";

        /// <summary>
        /// Whether the registered path can only resolve beneath the root it is joined to. A
        /// refusal says which rule it broke, in terms a caller can show a user.
        ///
        /// Accepts either form a script path is stored in - a plain path, or a JSON document
        /// carrying one under ScriptPath. Both reach Path.Combine at dispatch, so checking only
        /// the plain form would leave the JSON form as an open door.
        /// </summary>
        public static PolicyDecision Check(string? scriptPath)
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                // Nothing to execute. Components legitimately carry no script.
                return PolicyDecision.Allow();
            }

            JsonNode? document;
            try
            {
                document = JsonNode.Parse(scriptPath);
            }
            catch (JsonException)
            {
                // The plain form. A path is never well-formed JSON, so this is the one parse
                // that decides which form the value is in.
                return CheckPath(scriptPath);
            }

            if (document is not JsonObject properties)
            {
                // Well-formed JSON that is not a document - a bare number or string - is not
                // the JSON form, which is always an object. It is judged as the path it is.
                return CheckPath(scriptPath);
            }

            var embedded = properties[ScriptPathProperty];

            if (embedded == null)
            {
                // Parsed cleanly and names no script. Dispatch combines an empty path with the
                // root, which resolves to the root itself.
                return PolicyDecision.Allow();
            }

            if (embedded is JsonValue value && value.TryGetValue<string>(out var embeddedPath))
            {
                return CheckPath(embeddedPath);
            }

            // ScriptPath is present but is not a string. Its confinement cannot be
            // established, and something that cannot be shown to be confined is refused.
            return PolicyDecision.Refuse(
                "it is a JSON script path whose ScriptPath is not a string, so it cannot be shown to"
                + " resolve beneath the script root.");
        }

        private static PolicyDecision CheckPath(string scriptPath)
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                return PolicyDecision.Allow();
            }

            var path = scriptPath.Trim();

            if (IsRooted(path))
            {
                return PolicyDecision.Refuse(
                    "it is an absolute path. Script paths are stored relative to the script root;"
                    + " an absolute path replaces that root instead of resolving beneath it.");
            }

            if (HasTraversalSegment(path))
            {
                return PolicyDecision.Refuse(
                    "it contains a parent directory reference ('..'), which can resolve outside"
                    + " the script root.");
            }

            if (path.Contains(':'))
            {
                // Everything colon-shaped in a Windows path names something other than a file
                // beneath the root: a drive, a device, or an alternate data stream. None of
                // them belongs in a registered script path.
                return PolicyDecision.Refuse(
                    "it contains a colon, which names a drive, device or alternate data stream"
                    + " rather than a file beneath the script root.");
            }

            return PolicyDecision.Allow();
        }

        private static bool IsRooted(string path)
        {
            // Judged directly rather than through Path.IsPathRooted, which answers according to
            // the host it runs on: on Linux it does not consider \\host\share or C:\x rooted at
            // all. The API is heading for a Linux host (#423), and this validation must give
            // the same answer there as the Windows host that will later join the path.
            if (path[0] == '\\' || path[0] == '/')
            {
                return true;
            }

            return path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
        }

        private static bool HasTraversalSegment(string path)
        {
            // Compared as whole segments: a file legitimately named "..dat" is not traversal,
            // and a rule that matched the substring would reject it.
            return path.Split('\\', '/').Any(segment => segment.Trim() == "..");
        }
    }
}

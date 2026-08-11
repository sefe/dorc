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
        /// <summary>
        /// True when the registered path can only resolve beneath the root it is joined to. On
        /// false, <paramref name="reason"/> says which rule it broke, in terms a caller can
        /// show a user.
        ///
        /// Accepts either form a script path is stored in - a plain path, or a JSON document
        /// carrying one under ScriptPath. Both reach Path.Combine at dispatch, so checking only
        /// the plain form would leave the JSON form as an open door.
        /// </summary>
        public static bool IsConfined(string? scriptPath, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                // Nothing to execute. Components legitimately carry no script.
                return true;
            }

            if (LooksLikeJson(scriptPath))
            {
                return IsEmbeddedPathConfined(scriptPath, out reason);
            }

            return IsRelativeAndTraversalFree(scriptPath, out reason);
        }

        private static bool IsEmbeddedPathConfined(string scriptPath, out string reason)
        {
            string? embedded;
            try
            {
                embedded = JsonNode.Parse(scriptPath)?["ScriptPath"]?.GetValue<string>();
            }
            catch (Exception)
            {
                // It announced itself as JSON and then would not yield a path - malformed, or
                // ScriptPath present but not a string. Its confinement cannot be established,
                // and something that cannot be shown to be confined is refused.
                reason = "it is a JSON script path whose ScriptPath cannot be read, so it cannot be"
                    + " shown to resolve beneath the script root.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(embedded))
            {
                // Parsed cleanly and names no script. Dispatch combines an empty path with the
                // root, which resolves to the root itself.
                reason = string.Empty;
                return true;
            }

            return IsRelativeAndTraversalFree(embedded, out reason);
        }

        /// <summary>
        /// Matches how the write path already classifies a stored value, so the two cannot
        /// disagree about which form they are looking at.
        /// </summary>
        private static bool LooksLikeJson(string scriptPath)
        {
            try
            {
                JsonNode.Parse(scriptPath);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsRelativeAndTraversalFree(string scriptPath, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                return true;
            }

            var path = scriptPath.Trim();

            if (IsRooted(path))
            {
                reason = "it is an absolute path. Script paths are stored relative to the script root;"
                    + " an absolute path replaces that root instead of resolving beneath it.";
                return false;
            }

            if (HasTraversalSegment(path))
            {
                reason = "it contains a parent directory reference ('..'), which can resolve outside"
                    + " the script root.";
                return false;
            }

            if (path.Contains(':'))
            {
                // Everything colon-shaped in a Windows path names something other than a file
                // beneath the root: a drive, a device, or an alternate data stream. None of
                // them belongs in a registered script path.
                reason = "it contains a colon, which names a drive, device or alternate data stream"
                    + " rather than a file beneath the script root.";
                return false;
            }

            return true;
        }

        private static bool IsRooted(string path)
        {
            // Judged directly rather than through Path.IsPathRooted, which answers according to
            // the host it runs on: on Linux it does not consider \\host\share or C:\x rooted at
            // all, and this validation must give the same answer everywhere it is evaluated as
            // the Windows host that will later join the path.
            if (path[0] == '\\' || path[0] == '/')
            {
                return true;
            }

            return path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
        }

        private static bool HasTraversalSegment(string path)
        {
            foreach (var segment in path.Split('\\', '/'))
            {
                // Compared as a whole segment: a file legitimately named "..dat" is not
                // traversal, and a rule that matched the substring would reject it.
                if (segment.Trim() == "..")
                {
                    return true;
                }
            }

            return false;
        }
    }
}

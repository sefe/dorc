using System;

namespace Dorc.ApiModel
{
    /// <summary>
    /// The host named by a URL or UNC path, read the one way every process reads it.
    ///
    /// Parsed rather than matched. A substring test against the URL text is satisfied by any
    /// attacker-chosen host that merely contains the expected one, so
    /// https://build.corp.example.com.attacker.net passes a test for "build.corp.example.com".
    /// Only comparing the parsed authority avoids that. <see cref="Uri"/> reads \\host\share,
    /// //host/share and file://host/share alike on every platform .NET 8 runs on, so there is
    /// one parser and one answer.
    ///
    /// Lives in the shared model assembly because the API, the Monitor and the Terraform Runner
    /// all need it and the Runner must not reference the data layer to get it.
    /// </summary>
    public static class SourceHost
    {
        /// <summary>
        /// The host, or null when none can be established. A local path such as file:///C:/x
        /// parses but names no host.
        /// </summary>
        public static string Of(string urlOrUncPath)
        {
            Uri uri;
            if (!Uri.TryCreate(urlOrUncPath?.Trim(), UriKind.Absolute, out uri))
            {
                return null;
            }

            return string.IsNullOrEmpty(uri.Host) ? null : uri.Host;
        }

        /// <summary>
        /// Whether the URL's host is <paramref name="host"/> or a subdomain of it. The suffix
        /// is matched on the parsed authority, so a path or query that merely ends in the same
        /// characters does not satisfy it.
        /// </summary>
        public static bool Is(string url, string host)
        {
            var actual = Of(url);

            return actual != null
                && (string.Equals(actual, host, StringComparison.OrdinalIgnoreCase)
                    || actual.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
        }
    }
}

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
        /// Normalises Windows extended-length local drive prefixes (\\?\C:\...) to standard
        /// drive paths (C:\...) and extended/NT device UNC prefixes
        /// (\\?\UNC\server\share, \\.\UNC\server\share, \??\UNC\server\share, plus '/' variants)
        /// into standard \\server\share syntax so System.Uri and Win32 agree on the path or host.
        /// </summary>
        public static string NormalizeExtendedUnc(string urlOrUncPath)
        {
            if (string.IsNullOrWhiteSpace(urlOrUncPath))
            {
                return urlOrUncPath;
            }

            var trimmed = urlOrUncPath.Trim();
            string normalized;
            if (TryStripExtendedLocalDrivePrefix(trimmed, out normalized))
            {
                return normalized;
            }

            if (TryStripExtendedUncPrefix(trimmed, out normalized))
            {
                return normalized;
            }

            if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = trimmed.Substring(5);
                var slashCount = 0;
                while (slashCount < 3 && slashCount < rest.Length && rest[slashCount] == '/')
                {
                    slashCount++;
                }

                if (TryStripExtendedUncPrefix(rest.Substring(slashCount), out normalized))
                {
                    return normalized;
                }
            }

            return trimmed;
        }

        /// <summary>
        /// True when the value names or resembles a remote authority, UNC path, device namespace
        /// path, WebDAV redirector UNC path, or file: URI with a non-local authority.
        /// Callers checking potentially remote paths must fail closed when this returns true and
        /// <see cref="TryExtractRemoteHost"/> returns false.
        /// </summary>
        public static bool IsRemoteOrUnc(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();

            if (TryStripExtendedLocalDrivePrefix(trimmed, out _))
            {
                return false;
            }

            if (trimmed.Length >= 2 && IsSlash(trimmed[0]) && IsSlash(trimmed[1]))
            {
                return true;
            }

            if (HasNtDeviceNamespacePrefix(trimmed))
            {
                return true;
            }

            if (Of(trimmed) != null)
            {
                return true;
            }

            if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (HasUserInfoInFileOrUncPath(trimmed))
                {
                    return true;
                }

                var normalized = NormalizeExtendedUnc(trimmed);
                if (!string.Equals(normalized, trimmed, StringComparison.Ordinal))
                {
                    return true;
                }

                Uri fileUri;
                if (!Uri.TryCreate(normalized, UriKind.Absolute, out fileUri))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(fileUri.Host) || fileUri.IsUnc)
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(fileUri.LocalPath)
                    && ((fileUri.LocalPath.Length >= 2 && IsSlash(fileUri.LocalPath[0]) && IsSlash(fileUri.LocalPath[1]))
                        || HasNtDeviceNamespacePrefix(fileUri.LocalPath)))
                {
                    return true;
                }

                return false;
            }

            return false;
        }

        /// <summary>
        /// Extracts the remote host from <paramref name="value"/> when it is remote/UNC and its
        /// authority can be unambiguously parsed. Returns false when <paramref name="value"/> is
        /// local OR when it is a remote/UNC syntax whose authority is ambiguous (e.g. WebDAV @SSL).
        /// </summary>
        public static bool TryExtractRemoteHost(string value, out string host)
        {
            host = null;
            if (!IsRemoteOrUnc(value))
            {
                return false;
            }

            host = Of(value);
            return !string.IsNullOrEmpty(host);
        }

        /// <summary>
        /// The host, or null when none can be established. A local path such as file:///C:/x
        /// parses but names no host; WebDAV redirector UNC authorities containing '@' and
        /// non-UNC device paths also return null so callers fail closed.
        /// </summary>
        public static string Of(string urlOrUncPath)
        {
            if (string.IsNullOrWhiteSpace(urlOrUncPath))
            {
                return null;
            }

            var trimmed = urlOrUncPath.Trim();
            if (TryStripExtendedLocalDrivePrefix(trimmed, out _))
            {
                return null;
            }

            var normalized = NormalizeExtendedUnc(trimmed);

            if (string.Equals(normalized, trimmed, StringComparison.Ordinal) && HasNonUncDevicePrefix(trimmed))
            {
                return null;
            }

            if (HasUserInfoInFileOrUncPath(normalized))
            {
                return null;
            }

            Uri uri;
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out uri))
            {
                return null;
            }

            return string.IsNullOrEmpty(uri.Host) ? null : uri.Host;
        }

        private static bool IsSlash(char c)
        {
            return c == '\\' || c == '/';
        }

        private static bool IsAsciiDriveLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        private static bool TryStripExtendedLocalDrivePrefix(string value, out string normalized)
        {
            normalized = null;

            // \\?\<DriveLetter>:\... and '/' variants (extended-length local DOS drive path)
            if (value.Length < 7
                || !IsSlash(value[0])
                || !IsSlash(value[1])
                || value[2] != '?'
                || !IsSlash(value[3])
                || !IsAsciiDriveLetter(value[4])
                || value[5] != ':'
                || !IsSlash(value[6])
                || value.IndexOf('@') >= 0)
            {
                return false;
            }

            var restIndex = 7;
            while (restIndex < value.Length && IsSlash(value[restIndex]))
            {
                restIndex++;
            }

            normalized = value.Substring(4, 2) + @"\" + value.Substring(restIndex).Replace('/', '\\');
            return true;
        }

        private static bool HasNtDeviceNamespacePrefix(string value)
        {
            return value.Length >= 3
                && IsSlash(value[0])
                && value[1] == '?'
                && value[2] == '?'
                && (value.Length == 3 || IsSlash(value[3]));
        }

        private static bool HasNonUncDevicePrefix(string value)
        {
            if (HasNtDeviceNamespacePrefix(value))
            {
                return true;
            }

            return value.Length >= 3
                && IsSlash(value[0])
                && IsSlash(value[1])
                && (value[2] == '?' || value[2] == '.')
                && (value.Length == 3 || IsSlash(value[3]));
        }

        private static bool TryStripExtendedUncPrefix(string value, out string normalized)
        {
            normalized = null;

            // \\?\UNC\server\share, \\.\UNC\server\share, and '/' variants
            if (value.Length >= 7
                && IsSlash(value[0])
                && IsSlash(value[1])
                && (value[2] == '?' || value[2] == '.')
                && IsSlash(value[3])
                && string.Compare(value, 4, "UNC", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
            {
                if (value.Length == 7)
                {
                    normalized = @"\\";
                    return true;
                }

                if (IsSlash(value[7]))
                {
                    normalized = @"\\" + value.Substring(8);
                    return true;
                }
            }

            // \??\UNC\server\share and '/' variants
            if (value.Length >= 7
                && IsSlash(value[0])
                && value[1] == '?'
                && value[2] == '?'
                && IsSlash(value[3])
                && string.Compare(value, 4, "UNC", 0, 3, StringComparison.OrdinalIgnoreCase) == 0)
            {
                if (value.Length == 7)
                {
                    normalized = @"\\";
                    return true;
                }

                if (IsSlash(value[7]))
                {
                    normalized = @"\\" + value.Substring(8);
                    return true;
                }
            }

            return false;
        }

        private static bool HasUserInfoInFileOrUncPath(string value)
        {
            if (value.Length >= 2 && IsSlash(value[0]) && IsSlash(value[1]))
            {
                return ExtractLeadingUncHostSegment(value).IndexOf('@') >= 0;
            }

            if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                var afterScheme = value.Substring(5);
                if (afterScheme.Length >= 2 && IsSlash(afterScheme[0]) && IsSlash(afterScheme[1]))
                {
                    if (afterScheme.Length == 2 || !IsSlash(afterScheme[2]))
                    {
                        var rest = afterScheme.Substring(2);
                        var slashIdx = IndexOfSlash(rest);
                        var hostPart = slashIdx >= 0 ? rest.Substring(0, slashIdx) : rest;
                        return hostPart.IndexOf('@') >= 0;
                    }

                    var afterThirdSlash = afterScheme.Substring(3);
                    if (afterThirdSlash.Length >= 2 && IsSlash(afterThirdSlash[0]) && IsSlash(afterThirdSlash[1]))
                    {
                        return ExtractLeadingUncHostSegment(afterThirdSlash).IndexOf('@') >= 0;
                    }
                }
            }

            return false;
        }

        private static string ExtractLeadingUncHostSegment(string uncValue)
        {
            var index = 0;
            while (index < uncValue.Length && IsSlash(uncValue[index]))
            {
                index++;
            }

            var rest = uncValue.Substring(index);
            var slashIdx = IndexOfSlash(rest);
            return slashIdx >= 0 ? rest.Substring(0, slashIdx) : rest;
        }

        private static int IndexOfSlash(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                if (IsSlash(value[i]))
                {
                    return i;
                }
            }

            return -1;
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

        /// <summary>
        /// The origin - scheme, host and explicit non-default port - or null when the URL names
        /// no host. Default ports are normalised away, so https://host and https://host:443 are
        /// the same origin.
        /// </summary>
        public static string OriginOf(string url)
        {
            if (Of(url) == null)
            {
                return null;
            }

            Uri uri;
            if (!Uri.TryCreate(NormalizeExtendedUnc(url.Trim()), UriKind.Absolute, out uri)
                || string.IsNullOrEmpty(uri.Host))
            {
                return null;
            }

            return uri.IsDefaultPort
                ? string.Format("{0}://{1}", uri.Scheme.ToLowerInvariant(), uri.Host.ToLowerInvariant())
                : string.Format("{0}://{1}:{2}", uri.Scheme.ToLowerInvariant(), uri.Host.ToLowerInvariant(), uri.Port);
        }

        /// <summary>
        /// Whether two URLs share the exact same scheme, host and normalised port.
        /// </summary>
        public static bool HasSameOrigin(string firstUrl, string secondUrl)
        {
            var first = OriginOf(firstUrl);
            var second = OriginOf(secondUrl);

            return first != null
                && second != null
                && string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }
}

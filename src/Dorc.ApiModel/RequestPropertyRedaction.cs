using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Dorc.ApiModel
{
    /// <summary>
    /// central helper for redacting <see cref="RequestProperty"/>
    /// values at emission boundaries. Used by every code path that surfaces
    /// per-deployment property values (logs, API responses, request-history
    /// grid, audit records) so that values flagged <see cref="RequestProperty.IsSensitive"/>
    /// are replaced with a redaction marker.
    ///
    /// This is the mechanism. Wiring at each of the seven  surfaces
    /// happens incrementally as those surfaces gain new emission paths in
    /// / /. The helper is single-source-of-truth for the
    /// redaction marker and the behaviour of each emission shape, so a
    /// future audit can grep for usage and find every redaction site at once.
    /// </summary>
    public static class RequestPropertyRedaction
    {
        /// <summary>
        /// Marker substituted for the value of a sensitive property. Matches
        /// the convention already used by Dorc.TerraformRunner.Logging.SensitivePropertyRedactor.
        /// </summary>
        public const string Marker = "[REDACTED]";

        /// <summary>
        /// Returns a new collection where every <see cref="RequestProperty"/>
        /// flagged sensitive has its <see cref="RequestProperty.PropertyValue"/>
        /// replaced with <see cref="Marker"/>. Non-sensitive entries are
        /// returned unchanged (same instance callers that mutate the input
        /// collection should clone first).
        /// </summary>
        public static IEnumerable<RequestProperty> RedactCollection(IEnumerable<RequestProperty> source)
        {
            if (source == null) yield break;
            foreach (var p in source.Where(p => p != null))
            {
                if (p.IsSensitive)
                {
                    yield return new RequestProperty
                    {
                        PropertyName = p.PropertyName,
                        PropertyValue = Marker,
                        IsSensitive = true,
                    };
                }
                else
                {
                    yield return p;
                }
            }
        }

        /// <summary>
        /// Formats the collection as a single newline-separated `name=value`
        /// string suitable for inclusion in a log message; sensitive values
        /// are redacted. Returns an empty string for null or empty input.
        /// </summary>
        public static string FormatForLog(IEnumerable<RequestProperty> source)
        {
            if (source == null) return string.Empty;
            var sb = new StringBuilder();
            var first = true;
            foreach (var p in source.Where(p => p != null))
            {
                if (!first) sb.Append('\n');
                first = false;
                sb.Append(p.PropertyName).Append('=');
                sb.Append(p.IsSensitive ? Marker : (p.PropertyValue ?? string.Empty));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Redacts sensitive property values inside a serialized
        /// DeploymentRequest.RequestDetails XML payload (the
        /// Dorc.Core.DeploymentRequestDetail shape, where each property is a
        /// PropertyPair element carrying Name/Value/IsSensitive). Intended
        /// for API emission boundaries only - the stored payload must keep
        /// cleartext values because the Monitor feeds them to the runner.
        /// Never write the result of this method back to the database.
        /// Payloads without a sensitive property (including all payloads
        /// persisted before the IsSensitive flag existed) are returned
        /// unchanged; malformed XML is returned unchanged rather than
        /// throwing at a read surface.
        /// </summary>
        public static string RedactRequestDetailsXml(string requestDetailsXml)
        {
            if (string.IsNullOrEmpty(requestDetailsXml)) return requestDetailsXml;
            // Cheap pre-check so the overwhelmingly common case (no sensitive
            // properties) skips XML parsing on hot list endpoints.
            if (requestDetailsXml.IndexOf("<IsSensitive>true</IsSensitive>", StringComparison.OrdinalIgnoreCase) < 0)
                return requestDetailsXml;

            try
            {
                var root = XElement.Parse(requestDetailsXml);
                foreach (var propertyPair in root.Descendants("PropertyPair"))
                {
                    var sensitiveElement = propertyPair.Element("IsSensitive");
                    if (sensitiveElement != null
                        && bool.TryParse(sensitiveElement.Value, out var isSensitive)
                        && isSensitive)
                    {
                        var valueElement = propertyPair.Element("Value");
                        if (valueElement != null) valueElement.Value = Marker;
                    }
                }
                return root.ToString(SaveOptions.DisableFormatting);
            }
            catch (XmlException)
            {
                return requestDetailsXml;
            }
        }
    }
}

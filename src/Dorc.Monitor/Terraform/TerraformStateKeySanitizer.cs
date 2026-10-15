using System.Text;

namespace Dorc.Monitor.Terraform
{
    // Single normalization for the two places that must agree on when two
    // (environment, component) pairs are "the same" terraform state:
    // the blob state key rendered by TerraformDispatcher and the
    // in-process TerraformConcurrencyGuard slot key. Injective modulo
    // case-folding: characters outside [a-z0-9-] are hex-escaped as
    // _XXXX (fixed four hex digits, so '_' itself becomes _005f and no
    // two distinct inputs can produce the same output). Two names can
    // therefore only share a key if they differ solely by case - and
    // environment/component names are unique case-insensitively in DOrc.
    public static class TerraformStateKeySanitizer
    {
        public static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "unknown";
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                var lower = char.ToLowerInvariant(c);
                if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
                {
                    sb.Append(lower);
                }
                else
                {
                    sb.Append('_').Append(((int)c).ToString("x4"));
                }
            }
            return sb.ToString();
        }
    }
}

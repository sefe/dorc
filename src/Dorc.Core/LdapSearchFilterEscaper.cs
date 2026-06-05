using System.Text;

namespace Dorc.Core
{
    /// <summary>
    /// Escapes user-supplied values for safe inclusion in an LDAP search filter,
    /// per RFC 4515 §3. Without escaping, characters such as <c>*</c>, <c>(</c>,
    /// <c>)</c> and <c>\</c> let a caller alter the logical structure of the filter
    /// (LDAP filter injection, CWE-90).
    /// </summary>
    public static class LdapSearchFilterEscaper
    {
        public static string? Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\5c"); break;
                    case '*': builder.Append("\\2a"); break;
                    case '(': builder.Append("\\28"); break;
                    case ')': builder.Append("\\29"); break;
                    case '\0': builder.Append("\\00"); break;
                    default: builder.Append(c); break;
                }
            }

            return builder.ToString();
        }
    }
}

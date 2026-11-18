using System.Text;

namespace Dorc.Core.VariableResolution
{
    public class PropertyParser
    {
        private const char EscapeCharacter = '$';

        public IEnumerable<Token> Parse(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                yield break;
            }

            var currentValue = new StringBuilder();
            var i = 0;

            while (i < value.Length)
            {
                if (value[i] != EscapeCharacter)
                {
                    currentValue.Append(value[i]);
                    i++;
                    continue;
                }

                // Already-escaped $$
                if (i + 1 < value.Length && value[i + 1] == EscapeCharacter)
                {
                    currentValue.Append(EscapeCharacter);
                    i += 2;
                    continue;
                }

                // Windows admin/hidden share like E$\, C$/, ADMIN$\, IPC$\ at start or after \ or /
                if (IsAdminShareDollarAt(value, i))
                {
                    currentValue.Append(EscapeCharacter);
                    i += 1;
                    continue;
                }

                // Standard $PropertyName$ token
                var nextDollar = FindClosingPropertyTokenDollar(value, i + 1);
                if (nextDollar > i + 1)
                {
                    var candidate = value.Substring(i + 1, nextDollar - (i + 1));
                    if (IsValidPropertyTokenName(candidate))
                    {
                        if (currentValue.Length > 0)
                        {
                            yield return new StaticToken(currentValue.ToString());
                            currentValue.Clear();
                        }

                        yield return new PropertyToken(candidate);
                        i = nextDollar + 1;
                        continue;
                    }
                }

                throw new InvalidOperationException($"Cannot parse property string '{value}'");
            }

            if (currentValue.Length > 0)
            {
                yield return new StaticToken(currentValue.ToString());
            }
        }

        private static int FindClosingPropertyTokenDollar(string value, int startIndex)
        {
            for (var j = startIndex; j < value.Length; j++)
            {
                var ch = value[j];
                if (ch == '\\' || ch == '/' || char.IsWhiteSpace(ch) || ch == '{' || ch == '}')
                {
                    return -1;
                }

                if (ch == EscapeCharacter)
                {
                    return j;
                }
            }

            return -1;
        }

        private static bool IsValidPropertyTokenName(string candidate)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                return false;
            }

            foreach (var ch in candidate)
            {
                if (ch == '\\' || ch == '/' || ch == EscapeCharacter || char.IsWhiteSpace(ch) || ch == '{' || ch == '}')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAdminShareDollarAt(string value, int dollarIndex)
        {
            // Must be followed by \, /, or end of string (and not another $)
            if (dollarIndex + 1 < value.Length)
            {
                var next = value[dollarIndex + 1];
                if (next != '\\' && next != '/')
                {
                    return false;
                }
            }

            // Check single drive letter before $: e.g. C$, E$ at index 0 or after \ or /
            if (dollarIndex >= 1 && char.IsLetter(value[dollarIndex - 1]))
            {
                if (dollarIndex == 1 || value[dollarIndex - 2] == '\\' || value[dollarIndex - 2] == '/')
                {
                    return true;
                }
            }

            // Check ADMIN$ or IPC$ at start or after \ or /
            foreach (var shareName in new[] { "ADMIN", "IPC" })
            {
                if (dollarIndex >= shareName.Length &&
                    string.Compare(value, dollarIndex - shareName.Length, shareName, 0, shareName.Length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    var prefixIdx = dollarIndex - shareName.Length;
                    if (prefixIdx == 0 || value[prefixIdx - 1] == '\\' || value[prefixIdx - 1] == '/')
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
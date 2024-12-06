using System.Collections.Generic;
using System.Linq;

namespace Tools.PropertyValueCreationCLI
{
    public class DeployCsvFileReader
    {
        public IEnumerable<PropValue> GetValues(string[] lines)
        {
            return lines
                .Select(l => l.Split(','))
                .Where(sl => !string.IsNullOrWhiteSpace(sl[0]))
                .Select(MergeInternalCommas)
                .Select(sl => new PropValue
                {
                    PropertyName = sl[0],
                    Environment = sl[3],
                    IsSecure = sl[1] == "1",
                    Value = sl[2]
                })
                .ToArray();
        }


        private string[] MergeInternalCommas(string[] parts)
        {
            var result = new List<string>();

            int k;
            for (k = 0; k < parts.Length - 1; k++)
                if (parts[k].FirstOrDefault() == '"' && parts[k + 1].LastOrDefault() == '"')
                {
                    result.Add(Unquote(parts[k] + "," + parts[k + 1])); //TEST ME
                    k++;
                }
                else
                {
                    result.Add(Unquote(parts[k]));
                }

            for (; k < parts.Length; k++) result.Add(Unquote(parts[k]));
            return result.ToArray();
        }

        private string Unquote(string originalValue)
        {
            if (originalValue.FirstOrDefault() == '"' && originalValue.LastOrDefault() == '"')
                return originalValue.Substring(1, originalValue.Length - 2);
            return originalValue;
        }
    }
}
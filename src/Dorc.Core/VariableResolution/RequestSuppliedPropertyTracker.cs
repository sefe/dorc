using System.Collections.Concurrent;

namespace Dorc.Core.VariableResolution
{
    /// <summary>
    /// Tracks which properties carry values supplied on a deployment request, and answers
    /// whether a given property's value is influenced by one.
    ///
    /// Property resolution ends by passing values to an expression evaluator that compiles
    /// and runs C#. That is safe for administrator-curated values and unsafe for anything a
    /// request can reach: a user able to submit a deployment request would otherwise execute
    /// code inside the process performing the resolution, which holds the deployment
    /// credentials. Resolvers consult this type to decide whether evaluation may proceed.
    ///
    /// Influence is transitive. A curated expression such as fn:"$X$" is
    /// administrator-authored, but if X resolves to request-supplied text then the string
    /// that gets compiled is partly attacker-authored. References are followed on the RAW
    /// stored value rather than the interpolated one, which is what makes the question
    /// decidable before any evaluation happens.
    /// </summary>
    public sealed class RequestSuppliedPropertyTracker
    {
        // Case-insensitive: property names are matched case-insensitively elsewhere in the
        // resolution chain, and a taint check evadable by changing case would be no check.
        private readonly ConcurrentDictionary<string, byte> _requestSupplied =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly PropertyParser _parser = new();

        public void Mark(string property)
        {
            if (!string.IsNullOrEmpty(property))
            {
                _requestSupplied[property] = 0;
            }
        }

        public bool IsMarked(string property) => _requestSupplied.ContainsKey(property);

        /// <summary>
        /// Whether <paramref name="property"/> holds request-supplied text, or interpolates
        /// a property that does.
        /// </summary>
        /// <param name="rawValueLookup">
        /// Returns the stored, uninterpolated string value of a property, or null when the
        /// property holds no string or is not present.
        /// </param>
        public bool IsInfluenced(string property, Func<string, string?> rawValueLookup)
        {
            if (_requestSupplied.IsEmpty)
            {
                return false;
            }

            return IsInfluenced(property, rawValueLookup,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private bool IsInfluenced(string property, Func<string, string?> rawValueLookup, HashSet<string> visited)
        {
            if (!visited.Add(property))
            {
                // A reference cycle. The safe answer when the graph cannot be resolved is to
                // report influence and withhold evaluation.
                return true;
            }

            if (_requestSupplied.ContainsKey(property))
            {
                return true;
            }

            var raw = rawValueLookup(property);
            if (raw == null)
            {
                return false;
            }

            foreach (var referenced in ReferencedProperties(raw))
            {
                if (IsInfluenced(referenced, rawValueLookup, visited))
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerable<string> ReferencedProperties(string raw)
        {
            var referenced = new List<string>();

            try
            {
                foreach (var token in _parser.Parse(raw))
                {
                    if (token is PropertyToken)
                    {
                        referenced.Add(token.Value);
                    }
                }
            }
            catch
            {
                // A value that cannot be parsed cannot be shown to be free of
                // request-supplied references. Report one so evaluation is withheld.
                return new[] { string.Empty };
            }

            return referenced;
        }
    }
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace Dorc.Core.VariableResolution
{
    public class PropertyExpressionEvaluator : IPropertyExpressionEvaluator
    {
        // Per-instance, not static. A process-wide cache keyed on expression text outlives
        // every request in the Monitor, so a value first computed while deploying to one
        // environment was returned unchanged when deploying to another - including from a
        // non-production deployment to a subsequent production one. The resolver constructs
        // one evaluator per request, so instance scope makes the cache per-request.
        private readonly ConcurrentDictionary<string, object> _compiledResults = new();

        private const string ExpressionMarker = "fn:";

        private readonly ILogger _logger;

        public PropertyExpressionEvaluator(ILogger<PropertyExpressionEvaluator> logger)
        {
            _logger = logger;
        }

        public object Evaluate(object value)
        {
            // Anchored at the start rather than searched for anywhere in the value. The
            // previous test accepted the marker at any position but always sliced from
            // index 3, so a value containing "fn:" elsewhere was mis-sliced into an
            // arbitrary fragment and compiled.
            if (value is string s && s.StartsWith(ExpressionMarker, StringComparison.Ordinal))
            {
                var exp = s.Substring(ExpressionMarker.Length).Trim();
                if (!string.IsNullOrEmpty(exp))
                {
                    if (_compiledResults.TryGetValue(exp, out var res))
                    {
                        return res;
                    }

                    var resolvedValue = CSharpScript.EvaluateAsync(exp).Result;
                    _compiledResults[exp] = resolvedValue;
                    return resolvedValue;
                }
            }

            return value;
        }
    }
}
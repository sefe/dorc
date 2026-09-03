using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Dorc.Core.VariableResolution
{
    public class PropertyExpressionEvaluator : IPropertyExpressionEvaluator
    {
        // Results of successfully parsed expressions. Per-instance, not static. A process-wide cache keyed on expression text outlives
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

                    // Parsed, not compiled. The C# scripting dependency is gone from this
                    // assembly: an estate inventory found every one of the 11 distinct
                    // expressions in use to be a string literal followed by a chain of
                    // lower-casing, upper-casing and replacement, which is a parser's job.
                    if (!PropertyExpressionGrammar.TryEvaluate(exp, out var resolvedValue, out var error))
                    {
                        // Fail closed. Never fall back to compilation - that fallback would be
                        // the weakness this step removes, and it is the only thing containing
                        // the part of the inventory that could not be examined, since secure
                        // configuration values are encrypted at rest and do reach here.
                        //
                        // The expression itself is not logged: it is a resolved property value
                        // and may be a secret. The error names the shape of the failure and
                        // where in the expression it occurred.
                        _logger.LogError("A property expression could not be parsed and was not evaluated.");

                        throw new PropertyExpressionEvaluationException(
                            $"A property expression could not be parsed and was not evaluated: {error}");
                    }

                    _compiledResults[exp] = resolvedValue;
                    return resolvedValue;
                }
            }

            return value;
        }
    }

    public sealed class PropertyExpressionEvaluationException : InvalidOperationException
    {
        public PropertyExpressionEvaluationException(string message)
            : base(message)
        {
        }
    }
}
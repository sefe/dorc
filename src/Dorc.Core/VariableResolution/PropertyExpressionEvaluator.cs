using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace Dorc.Core.VariableResolution
{
    public class PropertyExpressionEvaluator : IPropertyExpressionEvaluator
    {
        private static ConcurrentDictionary<string, object> _compiledResults = new ConcurrentDictionary<string, object>();

        private readonly ILogger _logger;

        public PropertyExpressionEvaluator(ILogger<PropertyExpressionEvaluator> logger)
        {
            _logger = logger;
        }

        // fn: expressions are pure string/arithmetic operations; a runaway
        // expression should never block a deployment thread indefinitely.
        private static readonly TimeSpan EvaluationTimeout = TimeSpan.FromSeconds(5);

        public object Evaluate(object value)
        {
            if (value is string s && s.Contains("fn:"))
            {
                var exp = s.Substring(3).Trim();
                if (!string.IsNullOrEmpty(exp))
                {
                    if (_compiledResults.TryGetValue(exp, out var res))
                    {
                        return res;
                    }

                    // Reject anything that is not a simple string/arithmetic
                    // expression BEFORE it reaches the scripting engine. This closes
                    // the arbitrary-code-execution vector: an expression such as
                    // fn:System.IO.File.ReadAllText("...") is refused rather than run.
                    if (!SafeExpressionValidator.IsSafe(exp, out var reason))
                    {
                        _logger.LogWarning("Refusing to evaluate fn: expression '{Expression}': {Reason}", exp, reason);
                        throw new InvalidOperationException($"Unsafe or invalid fn: expression: {reason}");
                    }

                    using var cts = new CancellationTokenSource(EvaluationTimeout);
                    var resolvedValue = CSharpScript.EvaluateAsync(exp, cancellationToken: cts.Token)
                        .GetAwaiter().GetResult();
                    _compiledResults[exp] = resolvedValue;
                    return resolvedValue;
                }
            }

            return value;
        }
    }
}
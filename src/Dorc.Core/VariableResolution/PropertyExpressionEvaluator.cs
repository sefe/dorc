using System.Collections.Concurrent;
using log4net;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace Dorc.Core.VariableResolution
{
    public class PropertyExpressionEvaluator : IPropertyExpressionEvaluator
    {
        private static ConcurrentDictionary<string, object> _compiledResults = new ConcurrentDictionary<string, object>();

        private readonly ILog _logger;

        public PropertyExpressionEvaluator(ILog logger)
        {
            _logger = logger;
        }

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

                    var resolvedValue = CSharpScript.EvaluateAsync(exp).Result;
                    _compiledResults[exp] = resolvedValue;
                    return resolvedValue;
                }
            }

            return value;
        }
    }
}